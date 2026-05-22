namespace IviCli.Domain.Devices;

/// <summary>
/// Errors that can arise from device-related operations in the domain.
/// </summary>
public abstract record DeviceError;

/// <summary>
/// A <see cref="DeviceName"/> could not be constructed from the given raw input
/// because it failed format validation (empty, too long, or not matching the
/// permitted character pattern).
/// </summary>
/// <param name="Raw">The raw input string as provided by the caller.</param>
public sealed record InvalidDeviceNameFormat(string Raw) : DeviceError;
