using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace IviCli.TestKit;

/// <summary>
/// An <see cref="ILogger{T}"/> that keeps every formatted message it is
/// given, so a test can assert that a component said something.
/// </summary>
public sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

    /// <summary>Every message logged so far, oldest first.</summary>
    public IReadOnlyList<(LogLevel Level, string Message)> Entries => [.. _entries];

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    ) => _entries.Enqueue((logLevel, formatter(state, exception)));
}
