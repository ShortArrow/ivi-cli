using IviCli.Application.Audit;
using IviCli.Application.Configuration;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;

namespace IviCli.Application.Devices;

/// <summary>
/// Application-layer handler for the <c>visa remove</c> command.
/// Removing the current default device cascade-clears the default per
/// <see cref="ConfigDocument.RemoveDevice(DeviceName)"/>.
/// </summary>
public sealed class RemoveDeviceCommandHandler
{
    private readonly IConfigStore _store;
    private readonly IAuditLog _audit;
    private readonly IAuditSubject _subject;
    private readonly TimeProvider _time;

    /// <summary>Creates a new handler bound to the supplied configuration store.</summary>
    public RemoveDeviceCommandHandler(
        IConfigStore store,
        IAuditLog? audit = null,
        IAuditSubject? subject = null,
        TimeProvider? time = null
    )
    {
        _store = store;
        _audit = audit ?? NullAuditLog.Instance;
        _subject = subject ?? new StaticAuditSubject("unknown");
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Validates the command, loads the configuration, removes the device,
    /// and persists the result.
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

        return Result.Success<DeviceName, RemoveDeviceError>(name);
    }

    private static Result<DeviceName, RemoveDeviceError> Fail(RemoveDeviceError error) =>
        Result.Failure<DeviceName, RemoveDeviceError>(error);
}
