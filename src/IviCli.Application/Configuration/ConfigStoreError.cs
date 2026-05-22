using IviCli.Domain;

namespace IviCli.Application.Configuration;

/// <summary>
/// Errors that an <see cref="IConfigStore"/> implementation can surface when
/// loading or saving a <see cref="Domain.Configuration.ConfigDocument"/>.
/// </summary>
public abstract record ConfigStoreError : IviError
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

/// <summary>
/// The store could not read its underlying medium (e.g. file missing, I/O
/// failure, permission denied).
/// </summary>
/// <param name="Reason">Human-readable reason, safe to log at Information.</param>
/// <param name="InnerException">Optional inner exception captured by the adapter; for diagnostic logs only.</param>
public sealed record ConfigStoreReadFailure(string Reason, Exception? InnerException = null)
    : ConfigStoreError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Error;

    /// <inheritdoc/>
    public override string Message => "config store read failed: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Reason };

    /// <inheritdoc/>
    public override Exception? Cause => InnerException;
}

/// <summary>
/// The store read its underlying medium successfully but the content could
/// not be parsed into a <see cref="Domain.Configuration.ConfigDocument"/>.
/// </summary>
/// <param name="Reason">A diagnostic description of the parse problem.</param>
public sealed record ConfigStoreParseFailure(string Reason) : ConfigStoreError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Error;

    /// <inheritdoc/>
    public override string Message => "config store parse failed: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Reason };
}

/// <summary>
/// The store failed to write to its underlying medium.
/// </summary>
/// <param name="Reason">Human-readable reason.</param>
/// <param name="InnerException">Optional inner exception captured by the adapter; for diagnostic logs only.</param>
public sealed record ConfigStoreWriteFailure(string Reason, Exception? InnerException = null)
    : ConfigStoreError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Error;

    /// <inheritdoc/>
    public override string Message => "config store write failed: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Reason };

    /// <inheritdoc/>
    public override Exception? Cause => InnerException;
}
