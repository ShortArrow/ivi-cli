using IviCli.Application.Capture;
using IviCli.Domain;
using IviCli.Domain.Devices;
using Microsoft.Extensions.Logging;

namespace IviCli.Application.Backends;

/// <summary>
/// <see cref="IBackendFactory"/> wrapper that decorates every resolved
/// <see cref="IIviBackend"/> with <see cref="CapturingBackend"/>. The
/// composition root installs this in front of
/// <c>DefaultBackendFactory</c> when traffic capture is enabled
/// (ADR 0031), so every transport — HiSlip / Vxi11 / Local / Socket /
/// Fake / Replay — participates without per-verb plumbing.
/// </summary>
public sealed class CapturingBackendFactory : IBackendFactory
{
    private readonly IBackendFactory _inner;
    private readonly ITrafficWriter _writer;
    private readonly ILogger<CapturingBackend>? _backendLogger;

    /// <summary>Wraps <paramref name="inner"/> so resolved backends are captured.</summary>
    public CapturingBackendFactory(
        IBackendFactory inner,
        ITrafficWriter writer,
        ILogger<CapturingBackend>? backendLogger = null
    )
    {
        _inner = inner;
        _writer = writer;
        _backendLogger = backendLogger;
    }

    /// <inheritdoc/>
    public Result<IIviBackend, BackendError> CreateFor(Device device)
    {
        var inner = _inner.CreateFor(device);
        if (inner is Result<IIviBackend, BackendError>.Ok { Value: var backend })
        {
            return Result.Success<IIviBackend, BackendError>(
                new CapturingBackend(backend, _writer, _backendLogger)
            );
        }
        return inner;
    }
}
