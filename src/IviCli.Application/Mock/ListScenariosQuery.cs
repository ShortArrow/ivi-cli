using System.Collections.Immutable;
using IviCli.Domain;
using IviCli.Domain.Mock;

namespace IviCli.Application.Mock;

/// <summary>Query DTO for <c>ivicli mock scenario list</c>.</summary>
public sealed record ListScenariosQuery;

/// <summary>The query result: every known scenario name, sorted.</summary>
public sealed record ScenarioListing(ImmutableArray<ScenarioName> Names);

/// <summary>Errors that the list query can fail with.</summary>
public abstract record ListScenariosError : IviError
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

/// <summary>The scenario store failed during list.</summary>
public sealed record ListScenariosStoreFailure(ScenarioStoreError Inner) : ListScenariosError
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
