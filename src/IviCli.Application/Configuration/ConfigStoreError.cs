namespace IviCli.Application.Configuration;

/// <summary>
/// Errors that an <see cref="IConfigStore"/> implementation can surface when
/// loading or saving a <see cref="Domain.Configuration.ConfigDocument"/>.
/// </summary>
public abstract record ConfigStoreError;

/// <summary>
/// The store could not read its underlying medium (e.g. file missing, I/O
/// failure, permission denied).
/// </summary>
/// <param name="Reason">Human-readable reason, safe to log at Information.</param>
/// <param name="Cause">Optional inner exception captured by the adapter; for diagnostic logs only.</param>
public sealed record ConfigStoreReadFailure(string Reason, Exception? Cause = null)
    : ConfigStoreError;

/// <summary>
/// The store read its underlying medium successfully but the content could
/// not be parsed into a <see cref="Domain.Configuration.ConfigDocument"/>.
/// </summary>
/// <param name="Reason">A diagnostic description of the parse problem.</param>
public sealed record ConfigStoreParseFailure(string Reason) : ConfigStoreError;

/// <summary>
/// The store failed to write to its underlying medium.
/// </summary>
/// <param name="Reason">Human-readable reason.</param>
/// <param name="Cause">Optional inner exception captured by the adapter; for diagnostic logs only.</param>
public sealed record ConfigStoreWriteFailure(string Reason, Exception? Cause = null)
    : ConfigStoreError;
