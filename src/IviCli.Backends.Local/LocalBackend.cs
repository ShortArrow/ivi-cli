using System.Threading.Channels;
using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;

namespace IviCli.Backends.Local;

/// <summary>
/// <see cref="IIviBackend"/> wired through an installed IVI VISA runtime
/// (NI-VISA / Keysight VISA). Concrete VISA calls are mediated by an
/// <see cref="IVisaSessionFactory"/> so unit tests can inject an
/// in-memory fake, and the project compiles without a vendor SDK.
/// </summary>
public sealed class LocalBackend : IIviBackend
{
    private readonly IVisaSessionFactory _factory;
    private readonly TimeSpan _openTimeout;
    private readonly Dictionary<DeviceName, LocalSession> _sessions = new();
    private readonly object _gate = new();

    /// <summary>
    /// Creates a backend that uses <paramref name="factory"/> to open
    /// VISA sessions with the supplied <paramref name="openTimeout"/>
    /// (default 5 s).
    /// </summary>
    public LocalBackend(IVisaSessionFactory factory, TimeSpan? openTimeout = null)
    {
        _factory = factory;
        _openTimeout = openTimeout ?? TimeSpan.FromSeconds(5);
    }

    /// <inheritdoc/>
    public Task<Result<Unit, BackendError>> OpenAsync(Device device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var openResult = _factory.Open(device.Resource, _openTimeout);
        if (openResult is not Result<IVisaSessionHandle, LocalVisaError>.Ok { Value: var handle })
        {
            var err = ((Result<IVisaSessionHandle, LocalVisaError>.Error)openResult).Err;
            return Task.FromResult(Fail(err));
        }
        lock (_gate)
        {
            if (_sessions.TryGetValue(device.Name, out var existing))
            {
                existing.Dispose();
            }
            _sessions[device.Name] = new LocalSession(handle, device.Name);
        }
        return Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));
    }

    /// <inheritdoc/>
    public Task<Result<Unit, BackendError>> CloseAsync(Device device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_sessions.Remove(device.Name, out var session))
            {
                session.Dispose();
            }
        }
        return Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));
    }

    /// <inheritdoc/>
    public Task<Result<Unit, BackendError>> WriteAsync(
        Device device,
        ScpiCommand command,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var handle = TryGetHandle(device);
        if (handle is null)
        {
            return Task.FromResult(
                Result.Failure<Unit, BackendError>(new TransportDisconnected("session not open"))
            );
        }
        var write = handle.Write(command.Value);
        if (write is Result<Unit, LocalVisaError>.Error err)
        {
            return Task.FromResult(Fail(err.Err));
        }
        return Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));
    }

    /// <inheritdoc/>
    public Task<Result<string, BackendError>> QueryAsync(
        Device device,
        ScpiQuery query,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var handle = TryGetHandle(device);
        if (handle is null)
        {
            return Task.FromResult(
                Result.Failure<string, BackendError>(new TransportDisconnected("session not open"))
            );
        }
        var queryResult = handle.Query(query.Value);
        return queryResult switch
        {
            Result<string, LocalVisaError>.Ok ok => Task.FromResult(
                Result.Success<string, BackendError>(ok.Value)
            ),
            Result<string, LocalVisaError>.Error err => Task.FromResult(FailString(err.Err)),
            _ => throw new InvalidOperationException("unknown Result variant"),
        };
    }

    /// <inheritdoc/>
    public Task<Result<string, BackendError>> ReadAsync(Device device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var handle = TryGetHandle(device);
        if (handle is null)
        {
            return Task.FromResult(
                Result.Failure<string, BackendError>(new TransportDisconnected("session not open"))
            );
        }
        var read = handle.Read();
        return read switch
        {
            Result<string, LocalVisaError>.Ok ok => Task.FromResult(
                Result.Success<string, BackendError>(ok.Value)
            ),
            Result<string, LocalVisaError>.Error err => Task.FromResult(FailString(err.Err)),
            _ => throw new InvalidOperationException("unknown Result variant"),
        };
    }

    /// <inheritdoc/>
    public Task<Result<Unit, BackendError>> TriggerAsync(Device device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var handle = TryGetHandle(device);
        if (handle is null)
        {
            return Task.FromResult(
                Result.Failure<Unit, BackendError>(new TransportDisconnected("session not open"))
            );
        }
        // v1 sends the SCPI *TRG common-command through the existing
        // Write path — works against every IEEE-488.2 instrument. A v2
        // can switch to IMessageBasedSession.AssertTrigger once the
        // IVisaSessionHandle port grows a Trigger() method (ADR 0041 §4).
        var write = handle.Write("*TRG");
        if (write is Result<Unit, LocalVisaError>.Error err)
        {
            return Task.FromResult(Fail(err.Err));
        }
        return Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ServiceRequest> ServiceRequestStream(
        Device device,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct
    )
    {
        var session = TryGetSession(device);
        if (session is null || !session.TryEnableServiceRequests())
        {
            // Best-effort per ADR 0041: no open session, or a VISA runtime
            // that refuses the subscription, yields an empty stream.
            yield break;
        }
        var reader = session.ServiceRequests.Reader;
        while (true)
        {
            bool ready;
            try
            {
                ready = await reader.WaitToReadAsync(ct);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            if (!ready)
            {
                yield break;
            }
            while (reader.TryRead(out var srq))
            {
                yield return srq;
            }
        }
    }

    private IVisaSessionHandle? TryGetHandle(Device device) => TryGetSession(device)?.Handle;

    private LocalSession? TryGetSession(Device device)
    {
        lock (_gate)
        {
            return _sessions.TryGetValue(device.Name, out var s) ? s : null;
        }
    }

    /// <summary>
    /// Per-Device session state: the VISA handle plus the SRQ fan-out
    /// channel its service request callback writes into.
    /// </summary>
    private sealed class LocalSession : IDisposable
    {
        private readonly DeviceName _device;
        private readonly object _srqGate = new();
        private bool _srqAttempted;
        private bool _srqEnabled;

        public LocalSession(IVisaSessionHandle handle, DeviceName device)
        {
            Handle = handle;
            _device = device;
        }

        public IVisaSessionHandle Handle { get; }

        public Channel<ServiceRequest> ServiceRequests { get; } =
            Channel.CreateUnbounded<ServiceRequest>();

        /// <summary>
        /// Subscribes to the handle's service requests on first call and
        /// reports whether SRQs can be delivered. Later calls replay the
        /// first outcome — the handle contract assumes a single consumer.
        /// </summary>
        public bool TryEnableServiceRequests()
        {
            lock (_srqGate)
            {
                if (_srqAttempted)
                {
                    return _srqEnabled;
                }
                _srqAttempted = true;
                _srqEnabled =
                    Handle.EnableServiceRequests(OnStatusByte) is Result<Unit, LocalVisaError>.Ok;
                return _srqEnabled;
            }
        }

        private void OnStatusByte(byte statusByte) =>
            ServiceRequests.Writer.TryWrite(
                new ServiceRequest(_device, statusByte, DateTimeOffset.UtcNow)
            );

        public void Dispose()
        {
            ServiceRequests.Writer.TryComplete();
            Handle.Dispose();
        }
    }

    private static Result<Unit, BackendError> Fail(LocalVisaError err) =>
        Result.Failure<Unit, BackendError>(MapError(err));

    private static Result<string, BackendError> FailString(LocalVisaError err) =>
        Result.Failure<string, BackendError>(MapError(err));

    private static BackendError MapError(LocalVisaError err) =>
        err switch
        {
            LocalVisaRuntimeMissing m => new TransportDisconnected(
                $"VISA runtime not available: {m.Detail}"
            ),
            LocalVisaOpenFailure o => new TransportDisconnected(
                $"open failed: {o.Detail}",
                o.Cause
            ),
            LocalVisaIoFailure i => new TransportDisconnected($"io failure: {i.Detail}", i.Cause),
            _ => new TransportDisconnected("unknown VISA failure"),
        };
}
