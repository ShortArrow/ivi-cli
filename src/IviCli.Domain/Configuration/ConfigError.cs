using IviCli.Domain.Devices;

namespace IviCli.Domain.Configuration;

/// <summary>
/// Errors that can arise from <see cref="ConfigDocument"/> mutation operations.
/// </summary>
public abstract record ConfigError : IviError
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
/// An attempt was made to add a device whose name is already present in the configuration.
/// </summary>
/// <param name="Name">The conflicting device name.</param>
public sealed record DuplicateDeviceName(DeviceName Name) : ConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "duplicate device name: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>
/// An operation referenced a device name that does not exist in the configuration.
/// </summary>
/// <param name="Name">The missing device name.</param>
public sealed record DeviceNotFound(DeviceName Name) : ConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "device not found: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>
/// An attempt was made to set the default device to a name that is not present
/// in the configuration's device collection.
/// </summary>
/// <param name="Name">The candidate default name that has no matching device.</param>
public sealed record DefaultDeviceMissing(DeviceName Name) : ConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "default device not configured: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}
