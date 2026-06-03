using IviCli.Domain;
using IviCli.Domain.Mock;

namespace IviCli.Application.Mock;

/// <summary>
/// Command DTO for adding a <see cref="MockRule"/> to a scene inside a
/// scenario. Exactly one of <see cref="Respond"/> / <see cref="Ack"/> /
/// <see cref="Fail"/> must be set; the handler validates that. The
/// <see cref="Scene"/> field is optional — when <see langword="null"/>
/// the rule is appended to the scenario's initial scene.
/// </summary>
/// <param name="ScenarioName">The owning scenario.</param>
/// <param name="Scene">
/// Target scene name. <see langword="null"/> means "the scenario's
/// initial scene" (the v0.1.x convenience).
/// </param>
/// <param name="Match">The exact SCPI text the new rule reacts to.</param>
/// <param name="Respond">Set when the action is <see cref="RuleAction.Respond"/>.</param>
/// <param name="Ack">Set to true when the action is <see cref="RuleAction.Ack"/>.</param>
/// <param name="Fail">Set when the action is <see cref="RuleAction.Fail"/> — the variant tag.</param>
/// <param name="FailDetail">Optional detail payload for fail variants that need one.</param>
/// <param name="TransitionTo">
/// Optional scene name to make current after the rule fires. The target
/// scene must exist in the scenario at <c>activate</c> time.
/// </param>
public sealed record AddRuleCommand(
    string ScenarioName,
    string? Scene,
    string Match,
    string? Respond,
    bool Ack,
    string? Fail,
    string? FailDetail,
    string? TransitionTo
);

/// <summary>Outcomes the add-rule command can fail with.</summary>
public abstract record AddRuleError : IviError
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
public sealed record AddRuleInvalidScenarioName(string Raw) : AddRuleError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid scenario name: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The supplied scene name failed validation.</summary>
public sealed record AddRuleInvalidSceneName(string Raw) : AddRuleError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid scene name: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The match text is empty or otherwise invalid.</summary>
public sealed record AddRuleInvalidMatch(string Raw) : AddRuleError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid match string: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>None or more than one of respond / ack / fail were supplied.</summary>
public sealed record AddRuleActionAmbiguous : AddRuleError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "specify exactly one of --respond, --ack, or --fail";
}

/// <summary>The scenario does not exist.</summary>
public sealed record AddRuleScenarioNotFound(ScenarioName Name) : AddRuleError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "scenario not found: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>The target scene does not exist within the scenario.</summary>
public sealed record AddRuleSceneNotFound(ScenarioName Scenario, SceneName Scene) : AddRuleError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "scene not found in scenario: {Scenario}/{Scene}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Scenario, Scene };
}

/// <summary>The scenario store could not be read or written.</summary>
public sealed record AddRuleStoreFailure(ScenarioStoreError Inner) : AddRuleError
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
