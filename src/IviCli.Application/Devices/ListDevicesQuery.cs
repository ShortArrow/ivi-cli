using System.Collections.Immutable;
using IviCli.Application.Configuration;
using IviCli.Domain.Devices;

namespace IviCli.Application.Devices;

/// <summary>
/// Query DTO for listing every configured device. The query is parameterless
/// today; pagination and filtering will be added when needs emerge.
/// </summary>
public sealed record ListDevicesQuery;

/// <summary>
/// The result of a successful <see cref="ListDevicesQuery"/>: the devices
/// (in their configured insertion order) plus the current default-device
/// pointer, if any.
/// </summary>
/// <param name="Devices">The configured devices.</param>
/// <param name="DefaultDevice">The current default device's name, or <see langword="null"/>.</param>
public sealed record DeviceListing(ImmutableArray<Device> Devices, DeviceName? DefaultDevice);

/// <summary>Errors that the <c>visa list</c> query can fail with.</summary>
public abstract record ListDevicesError;

/// <summary>
/// The underlying <see cref="IConfigStore"/> could not be loaded.
/// </summary>
/// <param name="Inner">The propagated <see cref="ConfigStoreError"/>.</param>
public sealed record ListDevicesStorageFailure(ConfigStoreError Inner) : ListDevicesError;
