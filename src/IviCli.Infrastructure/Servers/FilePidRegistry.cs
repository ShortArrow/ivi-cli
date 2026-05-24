using System.Collections.Immutable;
using System.Globalization;
using System.IO.Abstractions;
using IviCli.Application.Servers;
using IviCli.Domain;
using IviCli.Domain.Servers;

namespace IviCli.Infrastructure.Servers;

/// <summary>
/// File-backed <see cref="IServerProcessRegistry"/>. Each running gateway
/// writes a one-line PID file at
/// <c>&lt;state-dir&gt;/&lt;server-name&gt;.pid</c> with format
/// <c>&lt;pid&gt;\t&lt;iso8601-utc&gt;</c>. The directory is created on
/// first write.
/// </summary>
public sealed class FilePidRegistry : IServerProcessRegistry
{
    private const string PidFileExtension = ".pid";

    private readonly IFileSystem _fs;
    private readonly string _directory;

    /// <summary>Creates a registry rooted at <paramref name="directory"/>.</summary>
    public FilePidRegistry(IFileSystem fs, string directory)
    {
        _fs = fs;
        _directory = directory;
    }

    /// <inheritdoc/>
    public Task<Result<Unit, ServerProcessRegistryError>> WriteAsync(
        ServerName name,
        int processId,
        DateTimeOffset startedAt,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            if (!_fs.Directory.Exists(_directory))
            {
                _fs.Directory.CreateDirectory(_directory);
            }
            var path = PathFor(name);
            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"{processId}\t{startedAt.ToUniversalTime():O}"
            );
            _fs.File.WriteAllText(path, line);
            return Task.FromResult(Result.Success<Unit, ServerProcessRegistryError>(Unit.Value));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(
                Result.Failure<Unit, ServerProcessRegistryError>(
                    new ServerProcessRegistryIoFailure($"write failed at {_directory}", ex)
                )
            );
        }
    }

    /// <inheritdoc/>
    public Task<Result<ServerProcessEntry?, ServerProcessRegistryError>> ReadAsync(
        ServerName name,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var path = PathFor(name);
        if (!_fs.File.Exists(path))
        {
            return Task.FromResult(
                Result.Success<ServerProcessEntry?, ServerProcessRegistryError>(null)
            );
        }
        string raw;
        try
        {
            raw = _fs.File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(
                Result.Failure<ServerProcessEntry?, ServerProcessRegistryError>(
                    new ServerProcessRegistryIoFailure($"read failed at {path}", ex)
                )
            );
        }
        var parts = raw.Trim().Split('\t');
        if (
            parts.Length != 2
            || !int.TryParse(
                parts[0],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var pid
            )
            || !DateTimeOffset.TryParse(
                parts[1],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var startedAt
            )
        )
        {
            return Task.FromResult(
                Result.Failure<ServerProcessEntry?, ServerProcessRegistryError>(
                    new ServerProcessRegistryCorrupt(path, raw)
                )
            );
        }
        return Task.FromResult(
            Result.Success<ServerProcessEntry?, ServerProcessRegistryError>(
                new ServerProcessEntry(name, pid, startedAt)
            )
        );
    }

    /// <inheritdoc/>
    public Task<Result<Unit, ServerProcessRegistryError>> DeleteAsync(
        ServerName name,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var path = PathFor(name);
        if (!_fs.File.Exists(path))
        {
            return Task.FromResult(Result.Success<Unit, ServerProcessRegistryError>(Unit.Value));
        }
        try
        {
            _fs.File.Delete(path);
            return Task.FromResult(Result.Success<Unit, ServerProcessRegistryError>(Unit.Value));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(
                Result.Failure<Unit, ServerProcessRegistryError>(
                    new ServerProcessRegistryIoFailure($"delete failed at {path}", ex)
                )
            );
        }
    }

    /// <inheritdoc/>
    public Task<Result<ImmutableArray<ServerProcessEntry>, ServerProcessRegistryError>> ListAsync(
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        if (!_fs.Directory.Exists(_directory))
        {
            return Task.FromResult(
                Result.Success<ImmutableArray<ServerProcessEntry>, ServerProcessRegistryError>(
                    ImmutableArray<ServerProcessEntry>.Empty
                )
            );
        }
        var builder = ImmutableArray.CreateBuilder<ServerProcessEntry>();
        IEnumerable<string> paths;
        try
        {
            paths = _fs
                .Directory.EnumerateFiles(_directory, "*" + PidFileExtension)
                .OrderBy(p => p, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(
                Result.Failure<ImmutableArray<ServerProcessEntry>, ServerProcessRegistryError>(
                    new ServerProcessRegistryIoFailure($"list failed at {_directory}", ex)
                )
            );
        }
        foreach (var path in paths)
        {
            var stem = _fs.Path.GetFileNameWithoutExtension(path);
            if (
                ServerName.From(stem)
                is not Result<ServerName, ServerNameError>.Ok { Value: var name }
            )
            {
                // Skip malformed names; do not surface as a corruption error.
                continue;
            }
            var readResult = ReadAsync(name, ct).GetAwaiter().GetResult();
            if (
                readResult is Result<ServerProcessEntry?, ServerProcessRegistryError>.Ok
                {
                    Value: { } entry
                }
            )
            {
                builder.Add(entry);
            }
        }
        return Task.FromResult(
            Result.Success<ImmutableArray<ServerProcessEntry>, ServerProcessRegistryError>(
                builder.ToImmutable()
            )
        );
    }

    private string PathFor(ServerName name) =>
        _fs.Path.Combine(_directory, name.Value + PidFileExtension);
}
