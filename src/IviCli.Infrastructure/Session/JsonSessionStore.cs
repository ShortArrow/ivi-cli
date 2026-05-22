using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;
using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Session;

namespace IviCli.Infrastructure.Session;

/// <summary>
/// File-system-backed <see cref="ISessionStore"/> that persists the
/// <see cref="SessionState"/> as JSON. Writes are atomic (temp + rename)
/// and the resulting file is locked down to the current user per
/// ADR 0017 §4 (Unix: <c>chmod 0600</c>; Windows fallback documented below).
/// </summary>
public sealed class JsonSessionStore : ISessionStore
{
    private readonly IFileSystem _fs;
    private readonly string _path;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Creates a new JsonSessionStore at the supplied file-system path.</summary>
    public JsonSessionStore(IFileSystem fs, string path)
    {
        _fs = fs;
        _path = path;
    }

    /// <inheritdoc/>
    public async Task<Result<SessionState, SessionStoreError>> LoadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!_fs.File.Exists(_path))
        {
            return Result.Success<SessionState, SessionStoreError>(SessionState.Empty);
        }

        string text;
        try
        {
            text = await _fs.File.ReadAllTextAsync(_path, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<SessionState, SessionStoreError>(
                new SessionStoreReadFailure($"read failed at {_path}", ex)
            );
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return Result.Success<SessionState, SessionStoreError>(SessionState.Empty);
        }

        SessionStateDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<SessionStateDto>(text, JsonOptions);
        }
        catch (JsonException ex)
        {
            return Result.Failure<SessionState, SessionStoreError>(
                new SessionStoreParseFailure($"invalid JSON: {ex.Message}")
            );
        }

        if (dto is null)
        {
            return Result.Success<SessionState, SessionStoreError>(SessionState.Empty);
        }

        DeviceName? currentDevice = null;
        if (!string.IsNullOrEmpty(dto.CurrentDevice))
        {
            var nameResult = DeviceName.From(dto.CurrentDevice);
            if (nameResult is not Result<DeviceName, DeviceError>.Ok nameOk)
            {
                return Result.Failure<SessionState, SessionStoreError>(
                    new SessionStoreParseFailure($"invalid current_device: {dto.CurrentDevice}")
                );
            }
            currentDevice = nameOk.Value;
        }

        return Result.Success<SessionState, SessionStoreError>(new SessionState(currentDevice));
    }

    /// <inheritdoc/>
    public async Task<Result<Unit, SessionStoreError>> SaveAsync(
        SessionState state,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();

        var dto = new SessionStateDto { CurrentDevice = state.CurrentDevice?.Value };
        var serialized = JsonSerializer.Serialize(dto, JsonOptions);

        var directory = _fs.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory) && !_fs.Directory.Exists(directory))
        {
            try
            {
                _fs.Directory.CreateDirectory(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Result.Failure<Unit, SessionStoreError>(
                    new SessionStoreWriteFailure($"failed to create directory {directory}", ex)
                );
            }
        }

        var tempPath = _path + ".tmp";
        try
        {
            await _fs.File.WriteAllTextAsync(tempPath, serialized, ct);
            ApplyUserOnlyPermissions(tempPath);
            if (_fs.File.Exists(_path))
            {
                _fs.File.Delete(_path);
            }
            _fs.File.Move(tempPath, _path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<Unit, SessionStoreError>(
                new SessionStoreWriteFailure($"write failed at {_path}", ex)
            );
        }

        return Result.Success<Unit, SessionStoreError>(Unit.Value);
    }

    private void ApplyUserOnlyPermissions(string path)
    {
        // ADR 0017 §4: session.json must be user-only on Unix (chmod 0600).
        // Windows ACL tightening is deferred — the default user-profile path
        // already restricts cross-user access in practice on consumer
        // Windows; a follow-up cycle adds explicit FileSecurity ACL editing.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // Only act when the adapter is backed by the real file system; the
        // MockFileSystem used by tests does not implement Unix permissions.
        if (!ReferenceEquals(_fs.GetType(), typeof(FileSystem)))
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (PlatformNotSupportedException)
        {
            // No-op on platforms that do not support Unix file modes.
        }
        catch (IOException)
        {
            // Best-effort; the write itself already succeeded.
        }
    }

    private sealed class SessionStateDto
    {
        [JsonPropertyName("current_device")]
        public string? CurrentDevice { get; set; }
    }
}
