namespace IviCli.Application.Capture;

/// <summary>
/// Application-side reader port that streams <see cref="TrafficEvent"/>
/// records back from an NDJSON file produced by <see cref="ITrafficWriter"/>.
/// The async-enumerable signature keeps memory bounded for long sessions
/// — the bridge from capture to <c>ITrafficScenarioConverter</c>
/// (ADR 0033) consumes events one at a time.
/// </summary>
public interface INdjsonTrafficReader
{
    /// <summary>
    /// Yields each <see cref="TrafficEvent"/> in <paramref name="path"/>.
    /// Implementations should:
    /// <list type="bullet">
    /// <item>Skip blank lines and lines starting with <c>#</c>.</item>
    /// <item>Surface IO failures as exceptions (caller wraps).</item>
    /// <item>Open the file with shared-read access so a still-being-
    /// written capture can be read in parallel.</item>
    /// </list>
    /// </summary>
    IAsyncEnumerable<TrafficEvent> ReadAsync(string path, CancellationToken ct);
}
