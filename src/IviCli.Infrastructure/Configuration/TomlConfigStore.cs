using System.IO.Abstractions;
using IviCli.Application.Configuration;
using IviCli.Domain;
using IviCli.Domain.Configuration;

namespace IviCli.Infrastructure.Configuration;

/// <summary>
/// File-system-backed <see cref="IConfigStore"/> that persists the
/// configuration as TOML at a fixed path. Implements the Impureim Sandwich
/// pattern (ADR 0023 §5): I/O lives here, parsing is delegated to the pure
/// <see cref="TomlConfigParser"/>.
/// </summary>
public sealed class TomlConfigStore : IConfigStore
{
    private readonly IFileSystem _fs;
    private readonly string _path;

    /// <summary>Creates a new TomlConfigStore at the supplied file-system path.</summary>
    /// <param name="fs">File-system abstraction (per ADR 0010 §9.1).</param>
    /// <param name="path">Absolute path to the <c>config.toml</c> file.</param>
    public TomlConfigStore(IFileSystem fs, string path)
    {
        _fs = fs;
        _path = path;
    }

    /// <inheritdoc/>
    public async Task<Result<ConfigDocument, ConfigStoreError>> LoadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!_fs.File.Exists(_path))
        {
            return Result.Success<ConfigDocument, ConfigStoreError>(ConfigDocument.Empty);
        }

        string text;
        try
        {
            text = await _fs.File.ReadAllTextAsync(_path, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<ConfigDocument, ConfigStoreError>(
                new ConfigStoreReadFailure($"read failed at {_path}", ex)
            );
        }

        return TomlConfigParser.Parse(text);
    }

    /// <inheritdoc/>
    public async Task<Result<Unit, ConfigStoreError>> SaveAsync(
        ConfigDocument document,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();

        var serialized = TomlConfigParser.Serialize(document);
        var directory = _fs.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory) && !_fs.Directory.Exists(directory))
        {
            try
            {
                _fs.Directory.CreateDirectory(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Result.Failure<Unit, ConfigStoreError>(
                    new ConfigStoreWriteFailure($"failed to create directory {directory}", ex)
                );
            }
        }

        // Atomic write: stage to a sibling temp file, then move into place.
        var tempPath = _path + ".tmp";
        try
        {
            await _fs.File.WriteAllTextAsync(tempPath, serialized, ct);
            if (_fs.File.Exists(_path))
            {
                _fs.File.Delete(_path);
            }
            _fs.File.Move(tempPath, _path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<Unit, ConfigStoreError>(
                new ConfigStoreWriteFailure($"write failed at {_path}", ex)
            );
        }

        return Result.Success<Unit, ConfigStoreError>(Unit.Value);
    }
}
