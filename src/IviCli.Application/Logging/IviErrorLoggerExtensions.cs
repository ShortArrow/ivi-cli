using IviCli.Domain;
using Microsoft.Extensions.Logging;

namespace IviCli.Application.Logging;

/// <summary>
/// Logs an <see cref="IviError"/> through the contract the error itself carries:
/// its declared severity, cause, message template and structured arguments.
/// </summary>
public static class IviErrorLoggerExtensions
{
    /// <summary>Logs the error with its own severity, message template, arguments and cause.</summary>
    public static void LogIviError(this ILogger logger, IviError error)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(error);

        logger.Log(ToLogLevel(error.Severity), error.Cause, error.Message, error.LogArgs.ToArray());
    }

    /// <summary>Maps the project's <see cref="LogSeverity"/> to <see cref="LogLevel"/>.</summary>
    public static LogLevel ToLogLevel(LogSeverity severity) =>
        severity switch
        {
            LogSeverity.Trace => LogLevel.Trace,
            LogSeverity.Debug => LogLevel.Debug,
            LogSeverity.Information => LogLevel.Information,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Critical => LogLevel.Critical,
            _ => LogLevel.Information,
        };
}
