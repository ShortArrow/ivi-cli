using IviCli.Application.Logging;
using IviCli.Domain;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace IviCli.Application.Tests.Logging;

/// <summary>
/// Pins the error-to-logger contract: an error is logged with the severity,
/// message template, structured arguments and cause it carries, rather than a
/// fixed string chosen by the call site.
/// </summary>
public sealed class IviErrorLoggerExtensionsTests
{
    [Fact]
    public void Logs_at_the_severity_the_error_declares()
    {
        var logger = new RecordingLogger();

        logger.LogIviError(new WarningError("queued", 3));

        logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public void Passes_the_message_template_and_its_arguments_through()
    {
        var logger = new RecordingLogger();

        logger.LogIviError(new WarningError("queued", 3));

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Message.ShouldBe("waited {Seconds}s: {Reason}");
        entry.State.ShouldContain(pair => pair.Key == "Seconds" && Equals(pair.Value, 3));
        entry.State.ShouldContain(pair => pair.Key == "Reason" && Equals(pair.Value, "queued"));
    }

    [Fact]
    public void Carries_the_cause_for_diagnostics()
    {
        var logger = new RecordingLogger();
        var cause = new InvalidOperationException("socket closed");

        logger.LogIviError(new FailureError(cause));

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Exception.ShouldBeSameAs(cause);
    }

    private sealed record WarningError(string Reason, int Seconds) : IviError
    {
        public LogSeverity Severity => LogSeverity.Warning;
        public string Message => "waited {Seconds}s: {Reason}";
        public IReadOnlyList<object?> LogArgs => new object?[] { Seconds, Reason };
    }

    private sealed record FailureError(Exception Inner) : IviError
    {
        public LogSeverity Severity => LogSeverity.Error;
        public string Message => "transport failed";
        public Exception? Cause => Inner;
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<Entry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            var pairs =
                state as IReadOnlyList<KeyValuePair<string, object?>>
                ?? Array.Empty<KeyValuePair<string, object?>>();
            var template = pairs.FirstOrDefault(pair => pair.Key == "{OriginalFormat}").Value;
            Entries.Add(new Entry(logLevel, template as string ?? string.Empty, pairs, exception));
        }

        public sealed record Entry(
            LogLevel Level,
            string Message,
            IReadOnlyList<KeyValuePair<string, object?>> State,
            Exception? Exception
        );
    }
}
