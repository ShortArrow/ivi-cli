using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Mock;

namespace IviCli.Application.Mock;

/// <summary>Command DTO for activating a scenario (writes session.json).</summary>
public sealed record ActivateScenarioCommand(string Name);

/// <summary>Command DTO for deactivating any active scenario.</summary>
public sealed record DeactivateScenarioCommand;

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
