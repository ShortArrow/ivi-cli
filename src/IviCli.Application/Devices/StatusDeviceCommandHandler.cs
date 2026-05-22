using System.Diagnostics;
using IviCli.Application.Backends;
using IviCli.Application.Configuration;
using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;

namespace IviCli.Application.Devices;

/// <summary>
/// Application-layer handler for the <c>visa status</c> command (PRD §6.2).
/// Opens the resolved device, sends <c>*IDN?</c>, and reports the result with
/// the round-trip time. Transport errors are folded into the returned
/// <see cref="DeviceStatus"/> as <c>IsOnline = false</c> rather than as a
/// command-level failure, so the CLI can show an offline state cleanly.
/// </summary>
public sealed class StatusDeviceCommandHandler
{
    private static readonly ScpiQuery IdnQuery = ScpiQuery.From("*IDN?")
        is Result<ScpiQuery, ScpiError>.Ok idnOk
        ? idnOk.Value
        : throw new InvalidOperationException("*IDN? must be a valid SCPI query");

    private readonly IConfigStore _configStore;
    private readonly ISessionStore _sessionStore;
    private readonly IBackendFactory _backendFactory;

    /// <summary>Creates a new handler.</summary>
    public StatusDeviceCommandHandler(
        IConfigStore configStore,
        ISessionStore sessionStore,
        IBackendFactory backendFactory
    )
    {
        _configStore = configStore;
        _sessionStore = sessionStore;
        _backendFactory = backendFactory;
    }

    /// <summary>Probes the resolved device and returns its status snapshot.</summary>
    public async Task<Result<DeviceStatus, StatusDeviceError>> HandleAsync(
        StatusDeviceCommand command,
        CancellationToken ct
    )
    {
        var resolveResult = await DeviceResolver.ResolveAsync(
            command.Name,
            _configStore,
            _sessionStore,
            ct
        );
        if (resolveResult is not Result<Device, ResolveError>.Ok { Value: var device })
        {
            return MapResolveError(((Result<Device, ResolveError>.Error)resolveResult).Err);
        }

        var backendResult = _backendFactory.CreateFor(device);
        if (backendResult is not Result<IIviBackend, BackendError>.Ok { Value: var backend })
        {
            var err = ((Result<IIviBackend, BackendError>.Error)backendResult).Err;
            return Fail(new StatusDeviceBackendFailure(err));
        }

        var stopwatch = Stopwatch.StartNew();
        var openResult = await backend.OpenAsync(device, ct);
        if (openResult is not Result<Unit, BackendError>.Ok)
        {
            stopwatch.Stop();
            var err = ((Result<Unit, BackendError>.Error)openResult).Err;
            return Result.Success<DeviceStatus, StatusDeviceError>(
                new DeviceStatus(
                    device,
                    IsOnline: false,
                    ResponseTime: stopwatch.Elapsed,
                    IdnResponse: null,
                    FailureMessage: err.Message
                )
            );
        }

        try
        {
            var queryResult = await backend.QueryAsync(device, IdnQuery, ct);
            stopwatch.Stop();
            if (queryResult is not Result<string, BackendError>.Ok { Value: var idn })
            {
                var err = ((Result<string, BackendError>.Error)queryResult).Err;
                return Result.Success<DeviceStatus, StatusDeviceError>(
                    new DeviceStatus(
                        device,
                        IsOnline: false,
                        ResponseTime: stopwatch.Elapsed,
                        IdnResponse: null,
                        FailureMessage: err.Message
                    )
                );
            }

            return Result.Success<DeviceStatus, StatusDeviceError>(
                new DeviceStatus(
                    device,
                    IsOnline: true,
                    ResponseTime: stopwatch.Elapsed,
                    IdnResponse: idn,
                    FailureMessage: null
                )
            );
        }
        finally
        {
            _ = await backend.CloseAsync(device, ct);
        }
    }

    private static Result<DeviceStatus, StatusDeviceError> Fail(StatusDeviceError error) =>
        Result.Failure<DeviceStatus, StatusDeviceError>(error);

    private static Result<DeviceStatus, StatusDeviceError> MapResolveError(ResolveError error) =>
        error switch
        {
            ResolveError.ConfigFailure c => Fail(new StatusDeviceConfigFailure(c.Inner)),
            ResolveError.SessionFailure s => Fail(new StatusDeviceSessionFailure(s.Inner)),
            ResolveError.UserFailure
            {
                Failure: { Kind: DeviceResolver.FailureKind.InvalidName, RawName: var raw },
            } => Fail(new StatusDeviceInvalidName(raw ?? "")),
            ResolveError.UserFailure { Failure.Kind: DeviceResolver.FailureKind.NoTarget } => Fail(
                new StatusDeviceNoTarget()
            ),
            ResolveError.UserFailure
            {
                Failure: { Kind: DeviceResolver.FailureKind.UnknownDevice, ResolvedName: var name },
            } when name is not null => Fail(new StatusDeviceUnknown(name)),
            _ => Fail(new StatusDeviceNoTarget()),
        };
}
