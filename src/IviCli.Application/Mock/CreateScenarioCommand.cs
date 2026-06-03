using IviCli.Domain;
using IviCli.Domain.Mock;

namespace IviCli.Application.Mock;

/// <summary>
/// Command DTO for creating a new scenario. When
/// <paramref name="InitialScene"/> is supplied, the scenario starts
/// with that scene (empty, no rules) as both its only scene and its
/// initial scene. Otherwise the scenario starts with the synthetic
/// <c>default</c> scene (v0.1.x-compatible shape).
/// </summary>
public sealed record CreateScenarioCommand(
    string Name,
    string? IdnDefault = null,
    string? InitialScene = null
);

/// <summary>Outcomes the create command can fail with.</summary>
public abstract record CreateScenarioError : IviError
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

/// <summary>The supplied <c>--initial</c> scene name failed validation.</summary>
public sealed record CreateScenarioInvalidInitialScene(string Raw) : CreateScenarioError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid initial scene name: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The scenario name failed validation.</summary>
public sealed record CreateScenarioInvalidName(string Raw) : CreateScenarioError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid scenario name: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>A scenario with that name already exists.</summary>
public sealed record CreateScenarioAlreadyExists(ScenarioName Name) : CreateScenarioError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "scenario already exists: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>The scenario store could not be written.</summary>
public sealed record CreateScenarioStoreFailure(ScenarioStoreError Inner) : CreateScenarioError
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
