using IviCli.Application.Backends;
using IviCli.Application.Configuration;
using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Devices;

namespace IviCli.Application.Devices;

/// <summary>Command DTO for <c>visa write</c> (PRD §6.2).</summary>
/// <param name="Name">Optional device alias; null uses the current device.</param>
/// <param name="ScpiText">The raw SCPI command string.</param>
public sealed record WriteDeviceCommand(string? Name, string ScpiText);

/// <summary>Outcomes the <c>visa write</c> command can fail with.</summary>
public abstract record WriteDeviceError : IviError
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

/// <summary>The device alias could not be parsed.</summary>
public sealed record WriteDeviceInvalidName(string Raw) : WriteDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid device name: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The SCPI command failed validation.</summary>
public sealed record WriteDeviceInvalidScpi(string Raw, string Reason) : WriteDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid SCPI command: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Reason };
}

/// <summary>No explicit device was named and no current device is set.</summary>
public sealed record WriteDeviceNoTarget : WriteDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "no current device — use `visa use` first or pass a name";
}

/// <summary>The named device is not registered.</summary>
public sealed record WriteDeviceUnknown(DeviceName Name) : WriteDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "device not found: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>The configuration store could not be read.</summary>
public sealed record WriteDeviceConfigFailure(ConfigStoreError Inner) : WriteDeviceError
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

/// <summary>The session store could not be read.</summary>
public sealed record WriteDeviceSessionFailure(SessionStoreError Inner) : WriteDeviceError
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

/// <summary>A Backend / transport-level failure occurred during the write.</summary>
public sealed record WriteDeviceTransportFailure(BackendError Inner) : WriteDeviceError
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
