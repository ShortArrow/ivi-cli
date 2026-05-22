using IviCli.Domain;
using IviCli.Domain.Mock;

namespace IviCli.Application.Mock;

/// <summary>
/// Command DTO for adding a scene to a scenario. Exactly one of
/// <see cref="Respond"/> / <see cref="Ack"/> / <see cref="Fail"/> must be
/// non-default at the CLI; the handler validates that.
/// </summary>
/// <param name="ScenarioName">The owning scenario.</param>
/// <param name="Match">The exact SCPI text the new scene reacts to.</param>
/// <param name="Respond">Set when the action is <see cref="SceneAction.Respond"/>.</param>
/// <param name="Ack">Set to true when the action is <see cref="SceneAction.Ack"/>.</param>
/// <param name="Fail">Set when the action is <see cref="SceneAction.Fail"/> — the variant tag.</param>
/// <param name="FailDetail">Optional detail payload for fail variants that need one.</param>
public sealed record AddSceneCommand(
    string ScenarioName,
    string Match,
    string? Respond,
    bool Ack,
    string? Fail,
    string? FailDetail
);

/// <summary>Outcomes the add-scene command can fail with.</summary>
public abstract record AddSceneError : IviError
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
public sealed record AddSceneInvalidScenarioName(string Raw) : AddSceneError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid scenario name: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The match text is empty or otherwise invalid.</summary>
public sealed record AddSceneInvalidMatch(string Raw) : AddSceneError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid match string: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>None or more than one of respond / ack / fail were supplied.</summary>
public sealed record AddSceneActionAmbiguous : AddSceneError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "specify exactly one of --respond, --ack, or --fail";
}

/// <summary>The scenario does not exist.</summary>
public sealed record AddSceneScenarioNotFound(ScenarioName Name) : AddSceneError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "scenario not found: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>The scenario store could not be read or written.</summary>
public sealed record AddSceneStoreFailure(ScenarioStoreError Inner) : AddSceneError
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
