using IviCli.Application.Configuration;
using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Devices;

namespace IviCli.Application.Devices;

/// <summary>Command DTO for <c>visa status</c> (PRD §6.2).</summary>
/// <param name="Name">Optional device alias; null uses the current device.</param>
public sealed record StatusDeviceCommand(string? Name);

/// <summary>
/// Status snapshot for a single device. Reports whether the open + IDN probe
/// succeeded, the elapsed round-trip time, and the raw IDN response.
/// </summary>
/// <param name="Device">The resolved <see cref="Device"/>.</param>
/// <param name="IsOnline">True when the *IDN? probe completed successfully.</param>
/// <param name="ResponseTime">Round-trip duration of the probe.</param>
/// <param name="IdnResponse">The instrument's response to <c>*IDN?</c>, when online.</param>
/// <param name="FailureMessage">A short user-facing message when offline.</param>
public sealed record DeviceStatus(
    Device Device,
    bool IsOnline,
    TimeSpan ResponseTime,
    string? IdnResponse,
    string? FailureMessage
);

/// <summary>Outcomes the <c>visa status</c> command can fail with at the Application level.</summary>
public abstract record StatusDeviceError : IviError
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
public sealed record StatusDeviceInvalidName(string Raw) : StatusDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid device name: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>No explicit device was named and no current device is set.</summary>
public sealed record StatusDeviceNoTarget : StatusDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "no current device — use `visa use` first or pass a name";
}

/// <summary>The named device is not registered.</summary>
public sealed record StatusDeviceUnknown(DeviceName Name) : StatusDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "device not found: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>The configuration store could not be read.</summary>
public sealed record StatusDeviceConfigFailure(ConfigStoreError Inner) : StatusDeviceError
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
public sealed record StatusDeviceSessionFailure(SessionStoreError Inner) : StatusDeviceError
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
