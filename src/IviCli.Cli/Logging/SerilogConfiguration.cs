using IviCli.Cli.Paths;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace IviCli.Cli.Logging;

/// <summary>
/// Builds the Serilog logger per ADR 0011: stderr console sink (human or
/// JSON) plus a rolling JSON file sink in the per-OS state directory.
/// </summary>
public static class SerilogConfiguration
{
    /// <summary>Options controlling sink levels and formats.</summary>
    public sealed record Options(
        LogEventLevel MinimumLevel,
        LogEventLevel ConsoleMinimumLevel,
        bool ConsoleJsonFormat,
        string? LogFileOverride
    );

    /// <summary>Builds a Serilog logger from <paramref name="options"/>.</summary>
    public static Serilog.ILogger Build(Options options)
    {
        var logFilePath =
            options.LogFileOverride ?? Path.Combine(IviPaths.ResolveLogDirectory(), "ivi-cli-.log");

        var directory = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var config = new LoggerConfiguration()
            .MinimumLevel.Is(options.MinimumLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net", LogEventLevel.Warning)
            .MinimumLevel.Override("Ivi.Visa", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                new CompactJsonFormatter(),
                logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                fileSizeLimitBytes: 100L * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: false
            );

        if (options.ConsoleJsonFormat)
        {
            config = config.WriteTo.Console(
                new CompactJsonFormatter(),
                restrictedToMinimumLevel: options.ConsoleMinimumLevel,
                standardErrorFromLevel: LogEventLevel.Verbose
            );
        }
        else
        {
            config = config.WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                restrictedToMinimumLevel: options.ConsoleMinimumLevel,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                standardErrorFromLevel: LogEventLevel.Verbose
            );
        }

        return config.CreateLogger();
    }

    /// <summary>
    /// Maps the project's <see cref="Domain.LogSeverity"/> to Serilog /
    /// MEL <see cref="LogEventLevel"/>.
    /// </summary>
    public static LogEventLevel ToLogEventLevel(Domain.LogSeverity severity) =>
        severity switch
        {
            Domain.LogSeverity.Trace => LogEventLevel.Verbose,
            Domain.LogSeverity.Debug => LogEventLevel.Debug,
            Domain.LogSeverity.Information => LogEventLevel.Information,
            Domain.LogSeverity.Warning => LogEventLevel.Warning,
            Domain.LogSeverity.Error => LogEventLevel.Error,
            Domain.LogSeverity.Critical => LogEventLevel.Fatal,
            _ => LogEventLevel.Information,
        };

    /// <summary>
    /// Maps the project's <see cref="Domain.LogSeverity"/> to
    /// <see cref="Microsoft.Extensions.Logging.LogLevel"/>.
    /// </summary>
    public static Microsoft.Extensions.Logging.LogLevel ToLogLevel(Domain.LogSeverity severity) =>
        severity switch
        {
            Domain.LogSeverity.Trace => Microsoft.Extensions.Logging.LogLevel.Trace,
            Domain.LogSeverity.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
            Domain.LogSeverity.Information => Microsoft.Extensions.Logging.LogLevel.Information,
            Domain.LogSeverity.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
            Domain.LogSeverity.Error => Microsoft.Extensions.Logging.LogLevel.Error,
            Domain.LogSeverity.Critical => Microsoft.Extensions.Logging.LogLevel.Critical,
            _ => Microsoft.Extensions.Logging.LogLevel.Information,
        };
}
