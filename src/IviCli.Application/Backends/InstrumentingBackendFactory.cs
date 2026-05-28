using System.Diagnostics;
using IviCli.Application.Telemetry;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;

namespace IviCli.Application.Backends;

/// <summary>
/// <see cref="IBackendFactory"/> decorator that emits one
/// <see cref="Activity"/> span per backend op and records the op's
/// duration into <see cref="IviCliTelemetry.BackendOpDurationMs"/>
/// (ADR 0040). Listeners attach via the OTel SDK in the composition
/// root; in their absence Activity creation is effectively free.
/// </summary>
public sealed class InstrumentingBackendFactory : IBackendFactory
{
    private readonly IBackendFactory _inner;

    /// <summary>Wraps <paramref name="inner"/> so resolved backends are instrumented.</summary>
    public InstrumentingBackendFactory(IBackendFactory inner)
    {
        _inner = inner;
    }

    /// <inheritdoc/>
    public Result<IIviBackend, BackendError> CreateFor(Device device)
    {
        var inner = _inner.CreateFor(device);
        if (inner is Result<IIviBackend, BackendError>.Ok { Value: var backend })
        {
            return Result.Success<IIviBackend, BackendError>(new InstrumentingBackend(backend));
        }
        return inner;
    }

    private sealed class InstrumentingBackend : IIviBackend
    {
        private readonly IIviBackend _inner;

        public InstrumentingBackend(IIviBackend inner) => _inner = inner;

        public async Task<Result<Unit, BackendError>> OpenAsync(Device device, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            using var activity = StartActivity("backend.open", device);
            var result = await _inner.OpenAsync(device, ct);
            Finalize(activity, sw, "open", device, result is Result<Unit, BackendError>.Ok);
            return result;
        }

        public async Task<Result<Unit, BackendError>> CloseAsync(
            Device device,
            CancellationToken ct
        )
        {
            var sw = Stopwatch.StartNew();
            using var activity = StartActivity("backend.close", device);
            var result = await _inner.CloseAsync(device, ct);
            Finalize(activity, sw, "close", device, result is Result<Unit, BackendError>.Ok);
            return result;
        }

        public async Task<Result<Unit, BackendError>> WriteAsync(
            Device device,
            ScpiCommand command,
            CancellationToken ct
        )
        {
            var sw = Stopwatch.StartNew();
            using var activity = StartActivity("backend.write", device);
            activity?.SetTag("scpi.text", command.Value);
            var result = await _inner.WriteAsync(device, command, ct);
            Finalize(activity, sw, "write", device, result is Result<Unit, BackendError>.Ok);
            return result;
        }

        public async Task<Result<string, BackendError>> QueryAsync(
            Device device,
            ScpiQuery query,
            CancellationToken ct
        )
        {
            var sw = Stopwatch.StartNew();
            using var activity = StartActivity("backend.query", device);
            activity?.SetTag("scpi.text", query.Value);
            var result = await _inner.QueryAsync(device, query, ct);
            Finalize(activity, sw, "query", device, result is Result<string, BackendError>.Ok);
            return result;
        }

        public async Task<Result<string, BackendError>> ReadAsync(
            Device device,
            CancellationToken ct
        )
        {
            var sw = Stopwatch.StartNew();
            using var activity = StartActivity("backend.read", device);
            var result = await _inner.ReadAsync(device, ct);
            Finalize(activity, sw, "read", device, result is Result<string, BackendError>.Ok);
            return result;
        }

        public async Task<Result<Unit, BackendError>> TriggerAsync(
            Device device,
            CancellationToken ct
        )
        {
            var sw = Stopwatch.StartNew();
            using var activity = StartActivity("backend.trigger", device);
            var result = await _inner.TriggerAsync(device, ct);
            Finalize(activity, sw, "trigger", device, result is Result<Unit, BackendError>.Ok);
            return result;
        }

        public IAsyncEnumerable<ServiceRequest> ServiceRequestStream(
            Device device,
            CancellationToken ct
        ) =>
            // SRQ delivery is server-push and out-of-band — instrumenting
            // each yield would inflate trace volume without per-event
            // semantic value. Pass through transparently for v1.
            _inner.ServiceRequestStream(device, ct);

        private static Activity? StartActivity(string name, Device device)
        {
            var activity = IviCliTelemetry.Backend.StartActivity(name, ActivityKind.Client);
            activity?.SetTag("ivi.device", device.Name.Value);
            return activity;
        }

        private static void Finalize(
            Activity? activity,
            Stopwatch sw,
            string op,
            Device device,
            bool ok
        )
        {
            sw.Stop();
            var outcome = ok ? "ok" : "error";
            IviCliTelemetry.BackendOpDurationMs.Record(
                sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("op", op),
                new KeyValuePair<string, object?>("ivi.device", device.Name.Value),
                new KeyValuePair<string, object?>("outcome", outcome)
            );
            if (activity is not null)
            {
                activity.SetTag("outcome", outcome);
                if (!ok)
                {
                    activity.SetStatus(ActivityStatusCode.Error);
                }
            }
        }
    }
}
