using System.Runtime.CompilerServices;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;

namespace IviCli.Application.Backends;

/// <summary>
/// <see cref="IBackendFactory"/> decorator that bounds every backend
/// operation by the device's timeout, so a silent instrument fails the
/// operation with <see cref="TransportTimeout"/> instead of holding the
/// caller until it cancels.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Write, query, read and trigger get the device timeout; a backend
/// marked <see cref="IEnforcesDeviceTimeout"/> gets
/// <see cref="EnforcedGrace"/> more, so its own timeout error arrives
/// first.</item>
/// <item>Opening gets the longer of the device timeout and
/// <see cref="OpenFloor"/>, since a short I/O timeout is not meant to cut
/// a network connect short.</item>
/// <item>After an operation times out, whether the deadline passed or the
/// backend reported <see cref="TransportTimeout"/> itself, the session is
/// closed and reopened before the next operation, so a reply that arrives
/// late is never read as the answer to a later request. The close gets
/// the operation's limit as well and is abandoned past it.</item>
/// <item>A cancellation from the caller propagates unchanged; only the
/// deadline turns into <see cref="TransportTimeout"/>.</item>
/// </list>
/// The service-request stream is not bounded: it waits for as long as
/// the caller listens, and outlives a session this decorator dropped by
/// subscribing again once the session is reopened.
/// </remarks>
public sealed class DeviceTimeoutBackendFactory : IBackendFactory
{
    /// <summary>The least time an open is given.</summary>
    public static readonly TimeSpan OpenFloor = TimeSpan.FromSeconds(5);

    /// <summary>Extra time given to a backend that enforces the timeout itself.</summary>
    public static readonly TimeSpan EnforcedGrace = TimeSpan.FromSeconds(1);

    private readonly IBackendFactory _inner;
    private readonly TimeProvider _time;

    /// <summary>Wraps <paramref name="inner"/>; deadlines run on <paramref name="time"/>.</summary>
    public DeviceTimeoutBackendFactory(IBackendFactory inner, TimeProvider time)
    {
        _inner = inner;
        _time = time;
    }

    /// <inheritdoc/>
    public Result<IIviBackend, BackendError> CreateFor(Device device)
    {
        var inner = _inner.CreateFor(device);
        return inner is Result<IIviBackend, BackendError>.Ok { Value: var backend }
            ? Result.Success<IIviBackend, BackendError>(new DeviceTimeoutBackend(backend, _time))
            : inner;
    }

    private sealed class DeviceTimeoutBackend : IIviBackend
    {
        private readonly IIviBackend _inner;
        private readonly TimeProvider _time;
        private readonly TimeSpan _grace;
        private int _sessionDropped;

        /// <summary>
        /// Completed while the session is usable; replaced by a pending one
        /// when this decorator drops the session, and completed again when
        /// the session is reopened or the caller closes it.
        /// </summary>
        private TaskCompletionSource _reopened = Completed();

        public DeviceTimeoutBackend(IIviBackend inner, TimeProvider time)
        {
            _inner = inner;
            _time = time;
            _grace = inner is IEnforcesDeviceTimeout ? EnforcedGrace : TimeSpan.Zero;
        }

        public Task<Result<Unit, BackendError>> OpenAsync(Device device, CancellationToken ct) =>
            Bounded(OpenLimit(device), t => _inner.OpenAsync(device, t), ct);

        public Task<Result<Unit, BackendError>> CloseAsync(Device device, CancellationToken ct)
        {
            Interlocked.Exchange(ref _sessionDropped, 0);
            Volatile.Read(ref _reopened).TrySetResult();
            return _inner.CloseAsync(device, ct);
        }

        public Task<Result<Unit, BackendError>> WriteAsync(
            Device device,
            ScpiCommand command,
            CancellationToken ct
        ) => Operation(device, t => _inner.WriteAsync(device, command, t), ct);

        public Task<Result<string, BackendError>> QueryAsync(
            Device device,
            ScpiQuery query,
            CancellationToken ct
        ) => Operation(device, t => _inner.QueryAsync(device, query, t), ct);

        public Task<Result<string, BackendError>> ReadAsync(Device device, CancellationToken ct) =>
            Operation(device, t => _inner.ReadAsync(device, t), ct);

        public Task<Result<Unit, BackendError>> TriggerAsync(Device device, CancellationToken ct) =>
            Operation(device, t => _inner.TriggerAsync(device, t), ct);

        public async IAsyncEnumerable<ServiceRequest> ServiceRequestStream(
            Device device,
            [EnumeratorCancellation] CancellationToken ct
        )
        {
            while (true)
            {
                var reopened = Volatile.Read(ref _reopened);
                await foreach (var srq in _inner.ServiceRequestStream(device, ct))
                {
                    yield return srq;
                }
                var next = Volatile.Read(ref _reopened);
                if (next == reopened && next.Task.IsCompleted || !await Reopened(next, ct))
                {
                    yield break;
                }
            }
        }

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static TaskCompletionSource Completed()
        {
            var signal = NewSignal();
            signal.SetResult();
            return signal;
        }

        /// <summary>
        /// Waits for <paramref name="signal"/>, completed when a session
        /// this decorator dropped is reopened; false if the caller stops
        /// listening first.
        /// </summary>
        private static async Task<bool> Reopened(TaskCompletionSource signal, CancellationToken ct)
        {
            try
            {
                await signal.Task.WaitAsync(ct);
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return false;
            }
        }

        private TimeSpan OperationLimit(Device device) => device.Timeout.Value + _grace;

        private TimeSpan OpenLimit(Device device) =>
            (device.Timeout.Value > OpenFloor ? device.Timeout.Value : OpenFloor) + _grace;

        private async Task<Result<T, BackendError>> Operation<T>(
            Device device,
            Func<CancellationToken, Task<Result<T, BackendError>>> op,
            CancellationToken ct
        )
        {
            if (Interlocked.CompareExchange(ref _sessionDropped, 0, 1) == 1)
            {
                var reopened = await OpenAsync(device, ct);
                if (reopened is Result<Unit, BackendError>.Error { Err: var openError })
                {
                    Interlocked.Exchange(ref _sessionDropped, 1);
                    return Result.Failure<T, BackendError>(openError);
                }
                Volatile.Read(ref _reopened).TrySetResult();
            }
            var result = await Bounded(OperationLimit(device), op, ct);
            if (result is Result<T, BackendError>.Error { Err: TransportTimeout })
            {
                await DropSession(device);
            }
            return result;
        }

        private async Task<Result<T, BackendError>> Bounded<T>(
            TimeSpan limit,
            Func<CancellationToken, Task<Result<T, BackendError>>> op,
            CancellationToken ct
        )
        {
            using var deadline = new CancellationTokenSource(limit, _time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
            var started = _time.GetTimestamp();
            Result<T, BackendError> result;
            try
            {
                result = await op(linked.Token);
            }
            catch (OperationCanceledException)
                when (deadline.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                return TimedOut<T>(started);
            }
            return
                result is Result<T, BackendError>.Error
                && deadline.IsCancellationRequested
                && !ct.IsCancellationRequested
                ? TimedOut<T>(started)
                : result;
        }

        /// <summary>
        /// Closes the session, giving up after <paramref name="limit"/>: a
        /// transport that answers requests in order, such as VXI-11, may
        /// still be busy with the request that timed out.
        /// </summary>
        private async Task CloseWithin(Device device, TimeSpan limit)
        {
            using var deadline = new CancellationTokenSource(limit, _time);
            try
            {
                _ = await _inner.CloseAsync(device, deadline.Token);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
        }

        private Result<T, BackendError> TimedOut<T>(long started) =>
            Result.Failure<T, BackendError>(new TransportTimeout(_time.GetElapsedTime(started)));

        /// <summary>
        /// Closes the session so the next operation reopens it. A fresh
        /// reopen signal goes up before the close ends the session's
        /// service-request stream, so a listener can tell this drop from a
        /// close by the caller.
        /// </summary>
        private async Task DropSession(Device device)
        {
            Interlocked.Exchange(ref _reopened, NewSignal());
            await CloseWithin(device, OperationLimit(device));
            Interlocked.Exchange(ref _sessionDropped, 1);
        }
    }
}
