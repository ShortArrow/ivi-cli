using IviCli.Domain;
using IviCli.Domain.Mock;

namespace IviCli.Application.Mock;

/// <summary>Command DTO for removing a scene by 1-based index.</summary>
public sealed record RemoveSceneCommand(string ScenarioName, int Index);

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

/// <summary>The supplied 1-based index does not refer to an existing scene.</summary>
public sealed record RemoveSceneIndexOutOfRange(int Index, int Available) : RemoveSceneError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "scene index {Index} out of range (1..{Available})";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Index, Available };
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
