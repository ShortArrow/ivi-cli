using IviCli.Application.Configuration;
using IviCli.Domain.Devices;

namespace IviCli.Application.Devices;

/// <summary>
/// Outcomes the <c>visa add</c> use case can fail with. Per ADR 0014 §1 these
/// are an externally-declared sum type prefixed with the use-case name to
/// avoid collisions at the top of the Application namespace.
/// </summary>
public abstract record AddDeviceError;

/// <summary>The candidate name failed <see cref="DeviceName"/> validation.</summary>
/// <param name="Raw">The raw name string as supplied.</param>
public sealed record AddDeviceInvalidName(string Raw) : AddDeviceError;

/// <summary>The candidate VISA resource failed parsing.</summary>
/// <param name="Raw">The raw resource string as supplied.</param>
public sealed record AddDeviceInvalidResource(string Raw) : AddDeviceError;

/// <summary>The candidate timeout failed validation (negative or above the maximum).</summary>
/// <param name="RawMilliseconds">The raw millisecond value as supplied.</param>
public sealed record AddDeviceInvalidTimeout(int RawMilliseconds) : AddDeviceError;

/// <summary>The configuration already contains a device with the supplied alias.</summary>
/// <param name="Name">The conflicting device alias.</param>
public sealed record AddDeviceNameTaken(DeviceName Name) : AddDeviceError;

/// <summary>
/// The underlying <see cref="IConfigStore"/> could not be read or written.
/// The inner error preserves the storage-layer detail for the diagnostic
/// logging path described in ADR 0014 §9.
/// </summary>
/// <param name="Inner">The propagated <see cref="ConfigStoreError"/>.</param>
public sealed record AddDeviceStorageFailure(ConfigStoreError Inner) : AddDeviceError;
