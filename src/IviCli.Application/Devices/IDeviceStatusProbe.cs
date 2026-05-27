using System.Diagnostics;
using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;

namespace IviCli.Application.Devices;

/// <summary>
/// Single-device "is this thing alive?" probe. Opens the device, runs
/// <c>*IDN?</c>, measures the round-trip, and closes the session.
/// All transport / backend failures collapse into
/// <see cref="DeviceStatus.IsOnline"/> = <see langword="false"/> so callers
/// (status snapshot, watch live table) can render an offline row instead
/// of branching on error unions.
/// </summary>
public interface IDeviceStatusProbe
{
    /// <summary>Probes <paramref name="device"/> once and returns its snapshot.</summary>
    Task<DeviceStatus> ProbeAsync(Device device, CancellationToken ct);
}

/// <summary>
/// Production implementation backed by <see cref="IBackendFactory"/>.
/// Lifted from <see cref="StatusDeviceCommandHandler"/> so both
/// <c>visa status</c> and <c>visa watch</c> reuse the same byte-for-byte
/// probe logic.
/// </summary>
public sealed class DefaultDeviceStatusProbe : IDeviceStatusProbe
{
    private static readonly ScpiQuery IdnQuery = ScpiQuery.From("*IDN?")
        is Result<ScpiQuery, ScpiError>.Ok idnOk
        ? idnOk.Value
        : throw new InvalidOperationException("*IDN? must be a valid SCPI query");

    private readonly IBackendFactory _backendFactory;

    /// <summary>Creates a probe that resolves backends via <paramref name="backendFactory"/>.</summary>
    public DefaultDeviceStatusProbe(IBackendFactory backendFactory)
    {
        _backendFactory = backendFactory;
    }

    /// <inheritdoc/>
    public async Task<DeviceStatus> ProbeAsync(Device device, CancellationToken ct)
    {
        var backendResult = _backendFactory.CreateFor(device);
        if (backendResult is not Result<IIviBackend, BackendError>.Ok { Value: var backend })
        {
            var err = ((Result<IIviBackend, BackendError>.Error)backendResult).Err;
            return new DeviceStatus(
                device,
                IsOnline: false,
                ResponseTime: TimeSpan.Zero,
                IdnResponse: null,
                FailureMessage: err.Message
            );
        }

        var stopwatch = Stopwatch.StartNew();
        var openResult = await backend.OpenAsync(device, ct);
        if (openResult is not Result<Unit, BackendError>.Ok)
        {
            stopwatch.Stop();
            var err = ((Result<Unit, BackendError>.Error)openResult).Err;
            return new DeviceStatus(
                device,
                IsOnline: false,
                ResponseTime: stopwatch.Elapsed,
                IdnResponse: null,
                FailureMessage: err.Message
            );
        }

        try
        {
            var queryResult = await backend.QueryAsync(device, IdnQuery, ct);
            stopwatch.Stop();
            if (queryResult is not Result<string, BackendError>.Ok { Value: var idn })
            {
                var err = ((Result<string, BackendError>.Error)queryResult).Err;
                return new DeviceStatus(
                    device,
                    IsOnline: false,
                    ResponseTime: stopwatch.Elapsed,
                    IdnResponse: null,
                    FailureMessage: err.Message
                );
            }
            return new DeviceStatus(
                device,
                IsOnline: true,
                ResponseTime: stopwatch.Elapsed,
                IdnResponse: idn,
                FailureMessage: null
            );
        }
        finally
        {
            _ = await backend.CloseAsync(device, ct);
        }
    }
}
