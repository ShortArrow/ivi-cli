namespace IviCli.Domain;

/// <summary>
/// Contract every domain / application error type implements so that the
/// composition root can log it uniformly. See ADR 0014 §9 for the full
/// error-to-logger interface and ADR 0011 for the logging configuration
/// that consumes it.
/// </summary>
public interface IviError
{
    /// <summary>
    /// Default per-variant log severity. The composition root may override
    /// this when reformulating the error at a layer boundary.
    /// </summary>
    LogSeverity Severity { get; }

    /// <summary>
    /// User-facing English message. May contain Serilog-style named
    /// placeholders (e.g. <c>"device {Name} not found"</c>); the corresponding
    /// values come from <see cref="LogArgs"/>.
    /// </summary>
    string Message { get; }

    /// <summary>Structured-logging placeholder values, in template order.</summary>
    IReadOnlyList<object?> LogArgs => Array.Empty<object?>();

    /// <summary>
    /// Optional underlying exception for diagnostic logging; never surfaced
    /// to user-facing output (per ADRs 0014 §9 and 0017 §3).
    /// </summary>
    Exception? Cause => null;
}
