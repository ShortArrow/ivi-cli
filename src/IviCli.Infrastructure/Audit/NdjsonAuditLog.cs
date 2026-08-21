using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
                new AuthSucceededWire(a.Kind, a.Timestamp, a.Mechanism, a.Subject, a.Transport),
                AuditJsonContext.Default.AuthSucceededWire
            ),
            AuthFailed a => JsonSerializer.Serialize(
                new AuthFailedWire(a.Kind, a.Timestamp, a.Mechanism, a.Reason, a.Transport),
                AuditJsonContext.Default.AuthFailedWire
            ),
            ConfigMutated c => JsonSerializer.Serialize(
                new ConfigMutatedWire(c.Kind, c.Timestamp, c.Operation, c.Target, c.Subject),
                AuditJsonContext.Default.ConfigMutatedWire
            ),
            ApiRequest r => JsonSerializer.Serialize(
                new ApiRequestWire(
                    r.Kind,
                    r.Timestamp,
                    r.Method,
                    r.Path,
                    r.Status,
                    r.Subject,
                    r.LatencyMs
                ),
                AuditJsonContext.Default.ApiRequestWire
            ),
            ServerLifecycle s => JsonSerializer.Serialize(
                new ServerLifecycleWire(s.Kind, s.Timestamp, s.Server, s.Action, s.Subject),
                AuditJsonContext.Default.ServerLifecycleWire
            ),
            _ => throw new InvalidOperationException(
                $"unsupported audit event variant: {ev.GetType().Name}"
            ),
        };
}

// Flattened wire shapes for the NDJSON lines. Named records instead of the
// earlier anonymous objects because the source-generated serializer below
// keeps this file off the reflection path (trim/AOT, issue #15); the keys
// on disk are unchanged.
internal sealed record AuthSucceededWire(
    string Kind,
    DateTimeOffset Timestamp,
    string Mechanism,
    string Subject,
    string Transport
);

internal sealed record AuthFailedWire(
    string Kind,
    DateTimeOffset Timestamp,
    string Mechanism,
    string Reason,
    string Transport
);

internal sealed record ConfigMutatedWire(
    string Kind,
    DateTimeOffset Timestamp,
    string Operation,
    string Target,
    string? Subject
);

internal sealed record ApiRequestWire(
    string Kind,
    DateTimeOffset Timestamp,
    string Method,
    string Path,
    int Status,
    string? Subject,
    [property: JsonPropertyName("latency_ms")] int LatencyMs
);

internal sealed record ServerLifecycleWire(
    string Kind,
    DateTimeOffset Timestamp,
    string Server,
    string Action,
    string? Subject
);

/// <summary>Source-generated serializer for the audit wire shapes (ADR 0040 / issue #15).</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(AuthSucceededWire))]
[JsonSerializable(typeof(AuthFailedWire))]
[JsonSerializable(typeof(ConfigMutatedWire))]
[JsonSerializable(typeof(ApiRequestWire))]
[JsonSerializable(typeof(ServerLifecycleWire))]
internal sealed partial class AuditJsonContext : JsonSerializerContext;
