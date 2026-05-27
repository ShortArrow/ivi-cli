using IviCli.Application.Configuration;
using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Devices;

namespace IviCli.Application.Devices;

/// <summary>
/// Application-layer handler for the <c>visa status</c> command (PRD §6.2).
/// Resolves the requested device alias and delegates the actual probe
/// (open + <c>*IDN?</c> + close + stopwatch) to <see cref="IDeviceStatusProbe"/>,
/// so the same probe path is shared with <c>visa watch</c>.
/// </summary>
public sealed class StatusDeviceCommandHandler
{
    private readonly IConfigStore _configStore;
    private readonly ISessionStore _sessionStore;
    private readonly IDeviceStatusProbe _probe;

    /// <summary>Creates a new handler.</summary>
    public StatusDeviceCommandHandler(
        IConfigStore configStore,
        ISessionStore sessionStore,
        IDeviceStatusProbe probe
    )
    {
        _configStore = configStore;
        _sessionStore = sessionStore;
        _probe = probe;
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

        var status = await _probe.ProbeAsync(device, ct);
        return Result.Success<DeviceStatus, StatusDeviceError>(status);
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
