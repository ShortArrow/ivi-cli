using IviCli.Application.Audit;
using IviCli.Application.Configuration;
using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Session;

namespace IviCli.Application.Devices;

/// <summary>
/// Application-layer handler for the <c>visa remove</c> command.
/// Removing the current default device cascade-clears the default per
/// <see cref="ConfigDocument.RemoveDevice(DeviceName)"/>, and removing a
/// device clears its scenario binding (ADR 0026 §16), so a device later
/// registered under the same name is not answered by the mock.
/// </summary>
public sealed class RemoveDeviceCommandHandler
{
    private readonly IConfigStore _store;
    private readonly ISessionStore _sessionStore;
    private readonly IAuditLog _audit;
    private readonly IAuditSubject _subject;
    private readonly TimeProvider _time;

    /// <summary>Creates a new handler bound to the supplied configuration store.</summary>
    public RemoveDeviceCommandHandler(
        IConfigStore store,
        ISessionStore sessionStore,
        IAuditLog? audit = null,
        IAuditSubject? subject = null,
        TimeProvider? time = null
    )
    {
        _store = store;
        _sessionStore = sessionStore;
        _audit = audit ?? NullAuditLog.Instance;
        _subject = subject ?? new StaticAuditSubject("unknown");
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Validates the command, loads the configuration and the session,
    /// removes the device, persists the result, and then clears the device's
    /// scenario binding. The device goes first because a failure in between
    /// then leaves a binding to a name that no device has, and not a
    /// registered device reaching its real resource instead of the mock.
    /// </summary>
    public async Task<Result<DeviceName, RemoveDeviceError>> HandleAsync(
        RemoveDeviceCommand command,
        CancellationToken ct
    )
    {
        if (
            DeviceName.From(command.Name)
            is not Result<DeviceName, DeviceError>.Ok { Value: var name }
        )
        {
            return Fail(new RemoveDeviceInvalidName(command.Name));
        }

        var loadResult = await _store.LoadAsync(ct);
        if (loadResult is not Result<ConfigDocument, ConfigStoreError>.Ok { Value: var config })
        {
            var loadErr = ((Result<ConfigDocument, ConfigStoreError>.Error)loadResult).Err;
            return Fail(new RemoveDeviceStorageFailure(loadErr));
        }

        var removeResult = config.RemoveDevice(name);
        if (removeResult is not Result<ConfigDocument, ConfigError>.Ok { Value: var updatedConfig })
        {
            return Fail(new RemoveDeviceNotFound(name));
        }

        var sessionResult = await _sessionStore.LoadAsync(ct);
        if (sessionResult is not Result<SessionState, SessionStoreError>.Ok { Value: var session })
        {
            var sessionErr = ((Result<SessionState, SessionStoreError>.Error)sessionResult).Err;
            return Fail(new RemoveDeviceSessionFailure(sessionErr));
        }

        var saveResult = await _store.SaveAsync(updatedConfig, ct);
        if (saveResult is not Result<Unit, ConfigStoreError>.Ok)
        {
            var saveErr = ((Result<Unit, ConfigStoreError>.Error)saveResult).Err;
            return Fail(new RemoveDeviceStorageFailure(saveErr));
        }

        await _audit.AppendAsync(
            new ConfigMutated(_time.GetUtcNow(), "device.remove", name.Value, _subject.Get()),
            ct
        );

        if (session.GetActiveScenario(name) is not null)
        {
            var unbindResult = await _sessionStore.SaveAsync(session.UnbindScenario(name), ct);
            if (unbindResult is Result<Unit, SessionStoreError>.Error unbindErr)
            {
                return Fail(new RemoveDeviceBindingNotCleared(name, unbindErr.Err));
            }
        }

        return Result.Success<DeviceName, RemoveDeviceError>(name);
    }

    private static Result<DeviceName, RemoveDeviceError> Fail(RemoveDeviceError error) =>
        Result.Failure<DeviceName, RemoveDeviceError>(error);
}
