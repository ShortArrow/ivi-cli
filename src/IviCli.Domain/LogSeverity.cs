namespace IviCli.Domain;

/// <summary>
/// Domain-level log-severity classification, decoupled from
/// <c>Microsoft.Extensions.Logging.LogLevel</c> so that <see cref="IviError"/>
/// can live in <c>IviCli.Domain</c> without taking on a MEL dependency.
/// The composition root maps this to <c>LogLevel</c> at log-emission time.
/// </summary>
public enum LogSeverity
{
    /// <summary>Highly detailed diagnostic information.</summary>
    Trace,

    /// <summary>Detailed diagnostic information, useful for troubleshooting.</summary>
    Debug,

    /// <summary>Business-event information that is normally retained.</summary>
    Information,

    /// <summary>Anomalies the application can recover from.</summary>
    Warning,

    /// <summary>Failures that prevent an operation from completing.</summary>
    Error,

    /// <summary>Failures requiring immediate attention; usually crashes or data loss.</summary>
    Critical,
}
