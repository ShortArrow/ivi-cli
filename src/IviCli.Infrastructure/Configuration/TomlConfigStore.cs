using System.IO.Abstractions;
using IviCli.Application.Configuration;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    private readonly ILogger _logger;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _warned = new(
        StringComparer.Ordinal
    );

    /// <summary>Creates a new TomlConfigStore at the supplied file-system path.</summary>
    /// <param name="fs">File-system abstraction (per ADR 0010 §9.1).</param>
    /// <param name="path">Absolute path to the <c>config.toml</c> file.</param>
    /// <param name="logger">
    /// Receives a warning for each loaded device whose terminator has no
    /// effect (ADR 0055 §5), once per device for the life of the store, since
    /// a long-running server loads the configuration for every request.
    /// </param>
    public TomlConfigStore(IFileSystem fs, string path, ILogger<TomlConfigStore>? logger = null)
    {
        _fs = fs;
        _path = path;
        _logger = logger ?? NullLogger<TomlConfigStore>.Instance;
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
            // Sharing delete lets a save replace the file while it is read,
            // which Windows otherwise refuses.
            using var stream = _fs.FileStream.New(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            using var reader = new StreamReader(stream);
            text = await reader.ReadToEndAsync(ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<ConfigDocument, ConfigStoreError>(
                new ConfigStoreReadFailure($"read failed at {_path}", ex)
            );
        }

        var parsed = TomlConfigParser.Parse(text);
        if (parsed is Result<ConfigDocument, ConfigStoreError>.Ok { Value: var config })
        {
            WarnAboutIgnoredTerminators(config);
        }
        return parsed;
    }

    private void WarnAboutIgnoredTerminators(ConfigDocument config)
    {
        foreach (var device in config.Devices)
        {
            if (device.IgnoresTermination && _warned.TryAdd(device.Name.Value, 0))
            {
                _logger.LogWarning(
                    "device {Device} sets a terminator, which its transport ignores: HiSLIP, VXI-11, USBTMC and GPIB end messages themselves",
                    device.Name.Value
                );
            }
        }
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

        // Atomic write: stage to a sibling temp file of its own, then replace
        // the file in one step, so a load never finds it missing; a missing
        // file loads as an empty configuration.
        var tempPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await _fs.File.WriteAllTextAsync(tempPath, serialized, ct);
            await ReplaceAsync(tempPath, ct);
        }
        catch (OperationCanceledException)
        {
            DeleteQuietly(tempPath);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeleteQuietly(tempPath);
            return Result.Failure<Unit, ConfigStoreError>(
                new ConfigStoreWriteFailure($"write failed at {_path}", ex)
            );
        }

        return Result.Success<Unit, ConfigStoreError>(Unit.Value);
    }

    /// <summary>Removes the temp file a failed or cancelled save left, if it can.</summary>
    private void DeleteQuietly(string tempPath)
    {
        try
        {
            _fs.File.Delete(tempPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Moves <paramref name="tempPath"/> over the configuration file, trying
    /// again briefly while another program, such as an editor, holds the file
    /// open without letting it be replaced.
    /// </summary>
    private async Task ReplaceAsync(string tempPath, CancellationToken ct)
    {
        const int Attempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                _fs.File.Move(tempPath, _path, overwrite: true);
                return;
            }
            catch (Exception ex)
                when (attempt < Attempts && ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10 * attempt), ct);
            }
        }
    }
}
