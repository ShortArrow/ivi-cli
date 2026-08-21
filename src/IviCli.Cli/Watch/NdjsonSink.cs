using System.Text.Json;
using IviCli.Application.Watch;

namespace IviCli.Cli.Watch;

/// <summary>
/// Newline-delimited JSON renderer for <c>visa watch --json</c>. One
/// JSON object per tick on stdout, suitable for piping into <c>jq</c>
/// or a streaming consumer.
/// </summary>
public sealed class NdjsonSink : IWatchDevicesSink
{
    private readonly TextWriter _writer;

    /// <summary>Creates a sink writing to <paramref name="writer"/> (default <see cref="Console.Out"/>).</summary>
    public NdjsonSink(TextWriter? writer = null)
    {
        _writer = writer ?? Console.Out;
    }

    /// <inheritdoc/>
    public Task EmitAsync(WatchTick tick, CancellationToken ct)
    {
        var dto = new WatchTickDto(
            tick.Timestamp,
            tick.Sequence,
            tick.Snapshots.Select(s => new SnapshotDto(
                s.Device.Name.Value,
                s.Device.Resource.ToLogString(),
                s.IsOnline,
                (int)s.ResponseTime.TotalMilliseconds,
                s.IdnResponse,
                s.FailureMessage
            ))
        );
        _writer.WriteLine(JsonSerializer.Serialize(dto, CliJsonContext.Default.WatchTickDto));
        _writer.Flush();
        return Task.CompletedTask;
    }

    internal sealed record WatchTickDto(
        DateTimeOffset Timestamp,
        int Sequence,
        IEnumerable<SnapshotDto> Snapshots
    );

    internal sealed record SnapshotDto(
        string Device,
        string Resource,
        bool Online,
        int LatencyMs,
        string? Idn,
        string? Error
    );
}
