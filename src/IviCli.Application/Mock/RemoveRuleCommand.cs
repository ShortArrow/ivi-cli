using IviCli.Domain;
using IviCli.Domain.Mock;

namespace IviCli.Application.Mock;

/// <summary>
/// Command DTO for removing a <see cref="MockRule"/> from a scene
/// inside a scenario, addressed by 1-based <paramref name="Index"/>
/// (as reported by <c>scenario show</c>).
/// </summary>
/// <param name="ScenarioName">The owning scenario.</param>
/// <param name="Scene">
/// Target scene name. <see langword="null"/> means "the scenario's
/// initial scene".
/// </param>
/// <param name="Index">1-based rule index inside the target scene.</param>
public sealed record RemoveRuleCommand(string ScenarioName, string? Scene, int Index);

/// <summary>Outcomes the remove-rule command can fail with.</summary>
public abstract record RemoveRuleError : IviError
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
public sealed record RemoveRuleInvalidScenarioName(string Raw) : RemoveRuleError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid scenario name: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The supplied scene name failed validation.</summary>
public sealed record RemoveRuleInvalidSceneName(string Raw) : RemoveRuleError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid scene name: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The scenario does not exist.</summary>
public sealed record RemoveRuleScenarioNotFound(ScenarioName Name) : RemoveRuleError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "scenario not found: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>The target scene does not exist within the scenario.</summary>
public sealed record RemoveRuleSceneNotFound(ScenarioName Scenario, SceneName Scene)
    : RemoveRuleError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "scene not found in scenario: {Scenario}/{Scene}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Scenario, Scene };
}

/// <summary>The supplied 1-based index is outside the rule list of the target scene.</summary>
public sealed record RemoveRuleIndexOutOfRange(int Index, int Available) : RemoveRuleError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "rule index out of range: {Index} (have {Available})";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Index, Available };
}

/// <summary>The scenario store could not be read or written.</summary>
public sealed record RemoveRuleStoreFailure(ScenarioStoreError Inner) : RemoveRuleError
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
