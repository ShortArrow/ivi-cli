using IviCli.Application.Configuration;
using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Devices;

namespace IviCli.Application.Devices;

/// <summary>
/// Outcomes the <c>visa remove</c> use case can fail with.
/// </summary>
public abstract record RemoveDeviceError : IviError
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

/// <summary>The candidate name failed <see cref="DeviceName"/> validation.</summary>
public sealed record RemoveDeviceInvalidName(string Raw) : RemoveDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid device name: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>No device with the supplied name is registered.</summary>
public sealed record RemoveDeviceNotFound(DeviceName Name) : RemoveDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "device not found: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>
/// The session could not be read, so nothing was changed: without it the
/// device's scenario binding cannot be cleared.
/// </summary>
public sealed record RemoveDeviceSessionFailure(SessionStoreError Inner) : RemoveDeviceError
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

/// <summary>
/// The device was removed, but its scenario binding could not be cleared,
/// so a device registered under the same name would answer from the mock
/// until the binding is deactivated.
/// </summary>
public sealed record RemoveDeviceBindingNotCleared(DeviceName Name, SessionStoreError Inner)
    : RemoveDeviceError
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

/// <summary>
/// The underlying <see cref="IConfigStore"/> could not be read or written.
/// </summary>
public sealed record RemoveDeviceStorageFailure(ConfigStoreError Inner) : RemoveDeviceError
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
