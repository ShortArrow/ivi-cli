using IviCli.Domain.Devices;

namespace IviCli.Domain.Configuration;

/// <summary>
/// Errors that can arise from <see cref="ConfigDocument"/> mutation operations.
/// </summary>
public abstract record ConfigError;

/// <summary>
/// An attempt was made to add a device whose name is already present in the configuration.
/// </summary>
/// <param name="Name">The conflicting device name.</param>
public sealed record DuplicateDeviceName(DeviceName Name) : ConfigError;

/// <summary>
/// An operation referenced a device name that does not exist in the configuration.
/// </summary>
/// <param name="Name">The missing device name.</param>
public sealed record DeviceNotFound(DeviceName Name) : ConfigError;

/// <summary>
/// An attempt was made to set the default device to a name that is not present
/// in the configuration's device collection.
/// </summary>
/// <param name="Name">The candidate default name that has no matching device.</param>
public sealed record DefaultDeviceMissing(DeviceName Name) : ConfigError;
