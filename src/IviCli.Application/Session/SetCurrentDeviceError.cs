using IviCli.Application.Configuration;
using IviCli.Domain;
using IviCli.Domain.Devices;

namespace IviCli.Application.Session;

/// <summary>Outcomes the <c>visa use</c> command can fail with.</summary>
public abstract record SetCurrentDeviceError : IviError
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
public sealed record SetCurrentDeviceInvalidName(string Raw) : SetCurrentDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid device name: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>No device with the supplied name is registered.</summary>
public sealed record SetCurrentDeviceUnknown(DeviceName Name) : SetCurrentDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "device not found: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>The configuration store could not be read or written.</summary>
public sealed record SetCurrentDeviceConfigFailure(ConfigStoreError Inner) : SetCurrentDeviceError
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

/// <summary>The session store could not be read or written.</summary>
public sealed record SetCurrentDeviceSessionFailure(SessionStoreError Inner) : SetCurrentDeviceError
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
