using System.Collections.Concurrent;
using System.Globalization;
using IviCli.Application.Watch;
using Spectre.Console;

namespace IviCli.Cli.Watch;

/// <summary>
/// Default <see cref="IWatchDevicesSink"/> renderer: a Spectre.Console
/// <see cref="LiveDisplayContext"/> driving an in-place
/// <see cref="Table"/>. Per-row colour highlights offline devices so
/// the operator can spot failures without re-reading the latency
/// column.
/// </summary>
public sealed class SpectreLiveTableSink : IWatchDevicesSink, IAsyncDisposable
{
    private readonly IAnsiConsole _console;
    private readonly Table _table;
    private readonly Task<int> _liveTask;
    private readonly TaskCompletionSource<LiveDisplayContext> _contextReady = new();
    private readonly TaskCompletionSource _shutdown = new();
    private readonly ConcurrentQueue<WatchTick> _pending = new();
    private readonly SemaphoreSlim _signal = new(0);

    /// <summary>Creates a sink writing to <paramref name="console"/> (default <see cref="AnsiConsole.Console"/>).</summary>
    public SpectreLiveTableSink(IAnsiConsole? console = null)
    {
        _console = console ?? AnsiConsole.Console;
        _table = BuildEmptyTable();
        _liveTask = _console
            .Live(_table)
            .AutoClear(false)
            .StartAsync(async ctx =>
            {
                _contextReady.SetResult(ctx);
                while (!_shutdown.Task.IsCompleted)
                {
                    await _signal.WaitAsync().ConfigureAwait(false);
                    while (_pending.TryDequeue(out var tick))
                    {
                        Render(tick);
                        ctx.Refresh();
                    }
                }
                return 0;
            });
    }

    /// <inheritdoc/>
    public async Task EmitAsync(WatchTick tick, CancellationToken ct)
    {
        await _contextReady.Task.ConfigureAwait(false);
        _pending.Enqueue(tick);
        _signal.Release();
        await Task.Yield();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        _shutdown.TrySetResult();
        _signal.Release();
        try
        {
            await _liveTask.ConfigureAwait(false);
        }
        catch
        {
            // Live display failures are not fatal to the shutdown path.
        }
    }

    private static Table BuildEmptyTable()
    {
        var t = new Table().Border(TableBorder.Rounded).Title("[bold]ivicli visa watch[/]");
        t.AddColumn("Device");
        t.AddColumn("Online");
        t.AddColumn(new TableColumn("Latency").RightAligned());
        t.AddColumn("IDN / Error");
        return t;
    }

    private void Render(WatchTick tick)
    {
        _table.Rows.Clear();
        foreach (var snap in tick.Snapshots)
        {
            var name = Markup.Escape(snap.Device.Name.Value);
            var online = snap.IsOnline ? "[green]yes[/]" : "[red]no[/]";
            var latency = $"{(int)snap.ResponseTime.TotalMilliseconds} ms";
            var idn = Markup.Escape(snap.IdnResponse ?? snap.FailureMessage ?? "");
            _table.AddRow(name, online, latency, idn);
        }
        _table.Caption = new TableTitle(
            $"tick {tick.Sequence.ToString(CultureInfo.InvariantCulture)} · "
                + tick.Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
        );
    }
}
