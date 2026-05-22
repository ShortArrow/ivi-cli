using IviCli.Domain;

namespace IviCli.Application.Mock;

/// <summary>
/// Errors that an <see cref="IScenarioStore"/> implementation can surface.
/// </summary>
public abstract record ScenarioStoreError : IviError
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

/// <summary>The store could not read its underlying medium.</summary>
public sealed record ScenarioStoreReadFailure(string Reason, Exception? InnerException = null)
    : ScenarioStoreError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Error;

    /// <inheritdoc/>
    public override string Message => "scenario store read failed: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Reason };

    /// <inheritdoc/>
    public override Exception? Cause => InnerException;
}

/// <summary>The content could not be parsed into a scenario.</summary>
public sealed record ScenarioStoreParseFailure(string Reason) : ScenarioStoreError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Error;

    /// <inheritdoc/>
    public override string Message => "scenario store parse failed: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Reason };
}

/// <summary>The store failed to write to its underlying medium.</summary>
public sealed record ScenarioStoreWriteFailure(string Reason, Exception? InnerException = null)
    : ScenarioStoreError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Error;

    /// <inheritdoc/>
    public override string Message => "scenario store write failed: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Reason };

    /// <inheritdoc/>
    public override Exception? Cause => InnerException;
}

/// <summary>The requested scenario does not exist in the store.</summary>
public sealed record ScenarioNotFound(string Name) : ScenarioStoreError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "scenario not found: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>The store already contains a scenario with the same name.</summary>
public sealed record ScenarioAlreadyExists(string Name) : ScenarioStoreError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "scenario already exists: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}
