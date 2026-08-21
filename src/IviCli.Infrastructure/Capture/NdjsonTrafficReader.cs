using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using IviCli.Application.Capture;

namespace IviCli.Infrastructure.Capture;

/// <summary>
/// File-backed <see cref="INdjsonTrafficReader"/> that streams
/// <see cref="TrafficEvent"/> records line-by-line. Symmetric with
/// <see cref="NdjsonTrafficWriter"/>: same JSON options, same enum
/// serialisation, so a writer output round-trips back through this
/// reader unchanged.
/// </summary>
public sealed class NdjsonTrafficReader : INdjsonTrafficReader
{
    private readonly IFileSystem _fs;

    /// <summary>Creates a reader rooted on <paramref name="fs"/>.</summary>
    public NdjsonTrafficReader(IFileSystem fs)
    {
        _fs = fs;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TrafficEvent> ReadAsync(
        string path,
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        using var stream = _fs.FileStream.New(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite
        );
        using var reader = new StreamReader(stream);
        var lineNumber = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null)
            {
                yield break;
            }
            lineNumber++;
            if (line.Length == 0 || line.AsSpan().TrimStart().StartsWith("#"))
            {
                continue;
            }
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            TrafficEvent? ev;
            try
            {
                ev = JsonSerializer.Deserialize(line, TrafficJsonContext.Default.TrafficEvent);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    $"NDJSON parse failure at {path}:{lineNumber}: {ex.Message}",
                    ex
                );
            }
            if (ev is not null)
            {
                yield return ev;
            }
        }
    }
}
