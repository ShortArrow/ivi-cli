using IviCli.Application.Configuration;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;

namespace IviCli.Application.Devices;

/// <summary>
/// Application-layer handler for the <c>visa add</c> command (per ADR 0003 §4
/// CQRS handler separation).
/// </summary>
/// <remarks>
/// The handler validates the raw <see cref="AddDeviceCommand"/> into Domain
/// Value Objects, loads the current <see cref="ConfigDocument"/>, applies the
/// addition (which enforces the cross-entity uniqueness invariant), and saves.
/// Each failure path is mapped at this layer's boundary into a typed
/// <see cref="AddDeviceError"/> variant (per ADR 0014 §5).
/// </remarks>
public sealed class AddDeviceCommandHandler
{
    private readonly IConfigStore _store;

    /// <summary>Creates a new handler bound to the supplied configuration store.</summary>
    public AddDeviceCommandHandler(IConfigStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Validates the command, loads the configuration, adds the device, and
    /// persists the result.
    /// </summary>
    public async Task<Result<DeviceName, AddDeviceError>> HandleAsync(
        AddDeviceCommand command,
        CancellationToken ct
    )
    {
        // Validate inputs into Domain Value Objects (Anti-Corruption Layer).
        if (
            DeviceName.From(command.Name)
            is not Result<DeviceName, DeviceError>.Ok { Value: var name }
        )
        {
            return Fail(new AddDeviceInvalidName(command.Name));
        }

        if (
            VisaResource.Parse(command.Resource)
            is not Result<VisaResource, VisaResourceError>.Ok { Value: var resource }
        )
        {
            return Fail(new AddDeviceInvalidResource(command.Resource));
        }

        if (
            Timeout.FromMilliseconds(command.TimeoutMilliseconds)
            is not Result<Timeout, TimeoutError>.Ok { Value: var timeout }
        )
        {
            return Fail(new AddDeviceInvalidTimeout(command.TimeoutMilliseconds));
        }

        // Load the current configuration.
        var loadResult = await _store.LoadAsync(ct);
        if (loadResult is not Result<ConfigDocument, ConfigStoreError>.Ok { Value: var config })
        {
            var loadErr = ((Result<ConfigDocument, ConfigStoreError>.Error)loadResult).Err;
            return Fail(new AddDeviceStorageFailure(loadErr));
        }

        // Apply the addition.
        var device = new Device(name, resource, timeout);
        var addResult = config.AddDevice(device);
        if (addResult is not Result<ConfigDocument, ConfigError>.Ok { Value: var updatedConfig })
        {
            var addErr = ((Result<ConfigDocument, ConfigError>.Error)addResult).Err;
            return Fail(MapConfigError(addErr, name));
        }

        // Persist.
        var saveResult = await _store.SaveAsync(updatedConfig, ct);
        if (saveResult is not Result<Unit, ConfigStoreError>.Ok)
        {
            var saveErr = ((Result<Unit, ConfigStoreError>.Error)saveResult).Err;
            return Fail(new AddDeviceStorageFailure(saveErr));
        }

        return Result.Success<DeviceName, AddDeviceError>(name);
    }

    private static Result<DeviceName, AddDeviceError> Fail(AddDeviceError error) =>
        Result.Failure<DeviceName, AddDeviceError>(error);

    private static AddDeviceError MapConfigError(ConfigError error, DeviceName candidate) =>
        error switch
        {
            DuplicateDeviceName d => new AddDeviceNameTaken(d.Name),
            // Other ConfigError variants (DeviceNotFound, DefaultDeviceMissing) are
            // impossible from AddDevice; fall back defensively.
            _ => new AddDeviceNameTaken(candidate),
        };
}
