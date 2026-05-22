using System.Collections.Immutable;
using IviCli.Application.Configuration;
using IviCli.Domain;
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
public abstract record ListDevicesError : IviError
{
    /// <inheritdoc/>
    public abstract LogSeverity Severity { get; }

    /// <inheritdoc/>
    public abstract string Message { get; }

    /// <inheritdoc/>
    public virtual IReadOnlyList<object?> LogArgs => Array.Empty<object?>();

    /// <inheritdoc/>
    public virtual Exception? Cause => null;
}

/// <summary>
/// The underlying <see cref="IConfigStore"/> could not be loaded.
/// </summary>
/// <param name="Inner">The propagated <see cref="ConfigStoreError"/>.</param>
public sealed record ListDevicesStorageFailure(ConfigStoreError Inner) : ListDevicesError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => Inner.Severity;

    /// <inheritdoc/>
    public override string Message => Inner.Message;

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => Inner.LogArgs;

    /// <inheritdoc/>
    public override Exception? Cause => Inner.Cause;
}
