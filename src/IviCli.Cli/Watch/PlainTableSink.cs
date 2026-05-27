using System.Globalization;
using System.Text;
using IviCli.Application.Watch;

namespace IviCli.Cli.Watch;

/// <summary>
/// ANSI-free <see cref="IWatchDevicesSink"/> renderer used by
/// <c>visa watch --plain</c>. Emits one self-contained snapshot per
/// tick so the output is safe to redirect into a log file or grep
/// from CI. Format is stable so snapshot tests can assert against it.
/// </summary>
public sealed class PlainTableSink : IWatchDevicesSink
{
    private readonly TextWriter _writer;

    /// <summary>Creates a sink writing to <paramref name="writer"/> (default <see cref="Console.Out"/>).</summary>
    public PlainTableSink(TextWriter? writer = null)
    {
        _writer = writer ?? Console.Out;
    }

    /// <inheritdoc/>
    public Task EmitAsync(WatchTick tick, CancellationToken ct)
    {
        var buffer = new StringBuilder();
        buffer
            .Append("# tick ")
            .Append(tick.Sequence.ToString(CultureInfo.InvariantCulture))
            .Append(" @ ")
            .AppendLine(tick.Timestamp.ToString("O", CultureInfo.InvariantCulture));
        buffer.AppendLine("Device              Online  Latency(ms)  IDN / Error");
        buffer.AppendLine(
            "------------------  ------  -----------  ------------------------------"
        );
        foreach (var snap in tick.Snapshots)
        {
            var name = snap.Device.Name.Value.PadRight(18);
            buffer.Append(name.AsSpan(0, 18));
            buffer.Append("  ");
            buffer.Append((snap.IsOnline ? "yes" : "no").PadRight(6));
            buffer.Append("  ");
            buffer.Append(
                ((int)snap.ResponseTime.TotalMilliseconds)
                    .ToString(CultureInfo.InvariantCulture)
                    .PadLeft(11)
            );
            buffer.Append("  ");
            buffer.AppendLine(snap.IdnResponse ?? snap.FailureMessage ?? "");
        }
        _writer.Write(buffer.ToString());
        _writer.Flush();
        return Task.CompletedTask;
    }
}
