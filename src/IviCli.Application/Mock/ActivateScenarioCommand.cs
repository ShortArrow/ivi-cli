using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Mock;

namespace IviCli.Application.Mock;

/// <summary>
/// Command DTO for activating a scenario, binding it to a specific
/// device. When <paramref name="Device"/> is <see langword="null"/>,
/// the handler binds to the session's current device
/// (<c>SessionState.CurrentDevice</c>); the call fails when no current
/// device is set and no explicit device was supplied.
/// </summary>
public sealed record ActivateScenarioCommand(string Name, string? Device = null);

/// <summary>
/// Command DTO for deactivating a scenario binding. Same device
/// resolution rules as <see cref="ActivateScenarioCommand"/>.
/// </summary>
public sealed record DeactivateScenarioCommand(string? Device = null);

/// <summary>Outcomes activate / deactivate can fail with.</summary>
public abstract record ActivateScenarioError : IviError
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

/// <summary>The supplied device name failed validation.</summary>
public sealed record ActivateScenarioInvalidDevice(string Raw) : ActivateScenarioError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid device name: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>
/// The caller did not supply a device and the session has no current
/// device selected — there's nothing to bind the scenario to.
/// </summary>
public sealed record ActivateScenarioNoDeviceSelected : ActivateScenarioError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message =>
        "no device selected: pass --for <device> or run `ivicli visa use <device>` first";
}

/// <summary>The scenario name failed validation.</summary>
public sealed record ActivateScenarioInvalidName(string Raw) : ActivateScenarioError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid scenario name: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The scenario does not exist.</summary>
public sealed record ActivateScenarioNotFound(ScenarioName Name) : ActivateScenarioError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "scenario not found: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>The scenario store could not be read.</summary>
public sealed record ActivateScenarioStoreFailure(ScenarioStoreError Inner) : ActivateScenarioError
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
public sealed record ActivateScenarioSessionFailure(SessionStoreError Inner) : ActivateScenarioError
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
