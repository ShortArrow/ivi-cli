using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IviCli.Application.Capture;

namespace IviCli.Infrastructure.Capture;

/// <summary>
/// File-backed <see cref="ITrafficWriter"/> that appends one NDJSON
/// line per event (ADR 0031). The file is opened in append + shared-read
/// mode so <c>tail -f</c> against it works on every supported platform.
/// A single <see cref="SemaphoreSlim"/> serialises writes so concurrent
/// verbs (e.g. visa watch's parallel probes) do not interleave bytes.
/// </summary>
public sealed class NdjsonTrafficWriter : ITrafficWriter, IDisposable
{
    private readonly IFileSystem _fs;
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Creates a writer that appends events to <paramref name="path"/>.</summary>
    public NdjsonTrafficWriter(IFileSystem fs, string path)
    {
        _fs = fs;
        _path = path;
    }

    /// <inheritdoc/>
    public async Task AppendAsync(TrafficEvent ev, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(ev, TrafficJsonContext.Default.TrafficEvent);
        var line = json + "\n";
        var bytes = Encoding.UTF8.GetBytes(line);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureDirectoryExists();
            using var stream = _fs.FileStream.New(
                _path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read
            );
            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Releases the internal synchronisation primitive.</summary>
    public void Dispose() => _gate.Dispose();

    private void EnsureDirectoryExists()
    {
        var directory = _fs.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory) && !_fs.Directory.Exists(directory))
        {
            _fs.Directory.CreateDirectory(directory);
        }
    }
}
