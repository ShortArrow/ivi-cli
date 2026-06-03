using IviCli.Domain;
using IviCli.Domain.Mock;

namespace IviCli.Application.Mock;

/// <summary>
/// Command DTO for removing a <see cref="MockScene"/> (state node) from
/// a scenario by its alias.
/// </summary>
/// <param name="ScenarioName">The owning scenario.</param>
/// <param name="SceneName">The scene alias to remove.</param>
public sealed record RemoveSceneCommand(string ScenarioName, string SceneName);

/// <summary>Outcomes the remove-scene command can fail with.</summary>
public abstract record RemoveSceneError : IviError
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
public sealed record RemoveSceneInvalidScenarioName(string Raw) : RemoveSceneError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid scenario name: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The scene name failed validation.</summary>
public sealed record RemoveSceneInvalidSceneName(string Raw) : RemoveSceneError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid scene name: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The scenario does not exist.</summary>
public sealed record RemoveSceneScenarioNotFound(ScenarioName Name) : RemoveSceneError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "scenario not found: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>The target scene does not exist within the scenario.</summary>
public sealed record RemoveSceneNotFound(ScenarioName Scenario, SceneName Scene) : RemoveSceneError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "scene not found in scenario: {Scenario}/{Scene}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Scenario, Scene };
}

/// <summary>The named scene is the scenario's initial scene and cannot be removed.</summary>
public sealed record RemoveSceneIsInitial(ScenarioName Scenario, SceneName Scene) : RemoveSceneError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message =>
        "scene {Scene} is the initial scene of {Scenario}; remove the scenario or change the initial scene first";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Scene, Scenario };
}

/// <summary>The scenario store could not be read or written.</summary>
public sealed record RemoveSceneStoreFailure(ScenarioStoreError Inner) : RemoveSceneError
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
