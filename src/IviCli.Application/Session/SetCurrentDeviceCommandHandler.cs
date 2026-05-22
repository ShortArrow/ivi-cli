using IviCli.Application.Configuration;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Session;

namespace IviCli.Application.Session;

/// <summary>
/// Application-layer handler for the <c>visa use</c> command. Updates the
/// session state's CurrentDevice; with <c>--default</c> (Persist=true) also
/// writes the alias into <c>config.toml</c>'s <c>[defaults]</c> section
/// (PRD §6.2 / §8.2).
/// </summary>
public sealed class SetCurrentDeviceCommandHandler
{
    private readonly IConfigStore _configStore;
    private readonly ISessionStore _sessionStore;

    /// <summary>Creates a new handler bound to the supplied stores.</summary>
    public SetCurrentDeviceCommandHandler(IConfigStore configStore, ISessionStore sessionStore)
    {
        _configStore = configStore;
        _sessionStore = sessionStore;
    }

    /// <summary>Validates, verifies device existence, and persists the change.</summary>
    public async Task<Result<DeviceName, SetCurrentDeviceError>> HandleAsync(
        SetCurrentDeviceCommand command,
        CancellationToken ct
    )
    {
        if (
            DeviceName.From(command.Name)
            is not Result<DeviceName, DeviceError>.Ok { Value: var name }
        )
        {
            return Fail(new SetCurrentDeviceInvalidName(command.Name));
        }

        var configResult = await _configStore.LoadAsync(ct);
        if (configResult is not Result<ConfigDocument, ConfigStoreError>.Ok { Value: var config })
        {
            var loadErr = ((Result<ConfigDocument, ConfigStoreError>.Error)configResult).Err;
            return Fail(new SetCurrentDeviceConfigFailure(loadErr));
        }

        if (config.FindDevice(name) is null)
        {
            return Fail(new SetCurrentDeviceUnknown(name));
        }

        if (command.Persist)
        {
            var defaultResult = config.SetDefaultDevice(name);
            if (defaultResult is not Result<ConfigDocument, ConfigError>.Ok { Value: var updated })
            {
                // SetDefaultDevice only fails when the name is missing — we
                // already verified existence — so this is defensive.
                return Fail(new SetCurrentDeviceUnknown(name));
            }
            var saveConfig = await _configStore.SaveAsync(updated, ct);
            if (saveConfig is not Result<Unit, ConfigStoreError>.Ok)
            {
                var saveErr = ((Result<Unit, ConfigStoreError>.Error)saveConfig).Err;
                return Fail(new SetCurrentDeviceConfigFailure(saveErr));
            }
        }

        var sessionResult = await _sessionStore.LoadAsync(ct);
        if (sessionResult is not Result<SessionState, SessionStoreError>.Ok { Value: var session })
        {
            var sessionErr = ((Result<SessionState, SessionStoreError>.Error)sessionResult).Err;
            return Fail(new SetCurrentDeviceSessionFailure(sessionErr));
        }

        var nextSession = session with { CurrentDevice = name };
        var saveSession = await _sessionStore.SaveAsync(nextSession, ct);
        if (saveSession is not Result<Unit, SessionStoreError>.Ok)
        {
            var saveErr = ((Result<Unit, SessionStoreError>.Error)saveSession).Err;
            return Fail(new SetCurrentDeviceSessionFailure(saveErr));
        }

        return Result.Success<DeviceName, SetCurrentDeviceError>(name);
    }

    private static Result<DeviceName, SetCurrentDeviceError> Fail(SetCurrentDeviceError error) =>
        Result.Failure<DeviceName, SetCurrentDeviceError>(error);
}
