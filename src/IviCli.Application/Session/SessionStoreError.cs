using IviCli.Domain;

namespace IviCli.Application.Session;

/// <summary>
/// Errors that an <see cref="ISessionStore"/> implementation can surface.
/// </summary>
public abstract record SessionStoreError : IviError
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
public sealed record SessionStoreReadFailure(string Reason, Exception? InnerException = null)
    : SessionStoreError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "session store read failed: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Reason };

    /// <inheritdoc/>
    public override Exception? Cause => InnerException;
}

/// <summary>The content could not be parsed into a <see cref="Domain.Session.SessionState"/>.</summary>
public sealed record SessionStoreParseFailure(string Reason) : SessionStoreError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "session store parse failed: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Reason };
}

/// <summary>The store failed to write to its underlying medium.</summary>
public sealed record SessionStoreWriteFailure(string Reason, Exception? InnerException = null)
    : SessionStoreError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Error;

    /// <inheritdoc/>
    public override string Message => "session store write failed: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Reason };

    /// <inheritdoc/>
    public override Exception? Cause => InnerException;
}
