using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using IviCli.Application.Audit;

namespace IviCli.Infrastructure.Audit;

/// <summary>
/// File-backed <see cref="IAuditLog"/> that appends one NDJSON line
/// per event (ADR 0043). Each variant's properties are flattened into
/// a single object so a downstream <c>jq</c> filter can match on
/// <c>kind</c> + the variant-specific fields without traversing a
/// nested envelope. The file is opened append + shared-read so
/// <c>tail -f</c> works cross-platform; writes are serialised under
/// a per-instance <see cref="SemaphoreSlim"/> so concurrent emitters
/// (gateway connection threads, API request middleware, config
/// mutators) never interleave bytes.
/// </summary>
public sealed class NdjsonAuditLog : IAuditLog, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private readonly IFileSystem _fs;
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Creates an audit log writing to <paramref name="path"/>.</summary>
    public NdjsonAuditLog(IFileSystem fs, string path)
    {
        _fs = fs;
        _path = path;
    }

    /// <inheritdoc/>
    public async Task AppendAsync(AuditEvent ev, CancellationToken ct)
    {
        var json = Serialize(ev);
        var bytes = Encoding.UTF8.GetBytes(json + "\n");

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

    private static string Serialize(AuditEvent ev) =>
        ev switch
        {
            AuthSucceeded a => JsonSerializer.Serialize(
                new
                {
                    kind = a.Kind,
                    timestamp = a.Timestamp,
                    mechanism = a.Mechanism,
                    subject = a.Subject,
                    transport = a.Transport,
                },
                JsonOptions
            ),
            AuthFailed a => JsonSerializer.Serialize(
                new
                {
                    kind = a.Kind,
                    timestamp = a.Timestamp,
                    mechanism = a.Mechanism,
                    reason = a.Reason,
                    transport = a.Transport,
                },
                JsonOptions
            ),
            ConfigMutated c => JsonSerializer.Serialize(
                new
                {
                    kind = c.Kind,
                    timestamp = c.Timestamp,
                    operation = c.Operation,
                    target = c.Target,
                    subject = c.Subject,
                },
                JsonOptions
            ),
            ApiRequest r => JsonSerializer.Serialize(
                new
                {
                    kind = r.Kind,
                    timestamp = r.Timestamp,
                    method = r.Method,
                    path = r.Path,
                    status = r.Status,
                    subject = r.Subject,
                    latency_ms = r.LatencyMs,
                },
                JsonOptions
            ),
            ServerLifecycle s => JsonSerializer.Serialize(
                new
                {
                    kind = s.Kind,
                    timestamp = s.Timestamp,
                    server = s.Server,
                    action = s.Action,
                    subject = s.Subject,
                },
                JsonOptions
            ),
            _ => throw new InvalidOperationException(
                $"unsupported audit event variant: {ev.GetType().Name}"
            ),
        };
}
