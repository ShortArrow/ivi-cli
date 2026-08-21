using System.Collections.Immutable;
using System.IO.Abstractions;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;
using IviCli.Domain.Session;

namespace IviCli.Infrastructure.Session;

/// <summary>
/// File-system-backed <see cref="ISessionStore"/> that persists the
/// <see cref="SessionState"/> as JSON. Writes are atomic (temp + rename)
/// and the resulting file is locked down to the current user per
/// ADR 0017 §4 (Unix: <c>chmod 0600</c>; Windows: a protected DACL).
/// </summary>
public sealed class JsonSessionStore : ISessionStore
{
    private readonly IFileSystem _fs;
    private readonly string _path;

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
            dto = JsonSerializer.Deserialize(text, SessionJsonContext.Default.SessionStateDto);
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

        var bindings = ImmutableDictionary.CreateBuilder<DeviceName, ScenarioName>();

        // v0.2.x → 0.2.4 migration: an old `active_scenario` field
        // promotes to the binding for the then-current device. When no
        // current device was set, the binding has nowhere to attach
        // and is dropped (the user can re-activate explicitly).
        if (!string.IsNullOrEmpty(dto.ActiveScenario))
        {
            var scenarioResult = ScenarioName.From(dto.ActiveScenario);
            if (scenarioResult is not Result<ScenarioName, ScenarioNameError>.Ok scenarioOk)
            {
                return Result.Failure<SessionState, SessionStoreError>(
                    new SessionStoreParseFailure($"invalid active_scenario: {dto.ActiveScenario}")
                );
            }
            if (currentDevice is not null)
            {
                bindings[currentDevice] = scenarioOk.Value;
            }
        }

        if (dto.DeviceScenarios is { } map)
        {
            foreach (var (deviceRaw, scenarioRaw) in map)
            {
                if (
                    DeviceName.From(deviceRaw)
                    is not Result<DeviceName, DeviceError>.Ok { Value: var deviceName }
                )
                {
                    return Result.Failure<SessionState, SessionStoreError>(
                        new SessionStoreParseFailure(
                            $"invalid device in device_scenarios: {deviceRaw}"
                        )
                    );
                }
                if (
                    ScenarioName.From(scenarioRaw)
                    is not Result<ScenarioName, ScenarioNameError>.Ok { Value: var scenarioName }
                )
                {
                    return Result.Failure<SessionState, SessionStoreError>(
                        new SessionStoreParseFailure(
                            $"invalid scenario in device_scenarios: {scenarioRaw}"
                        )
                    );
                }
                bindings[deviceName] = scenarioName;
            }
        }

        return Result.Success<SessionState, SessionStoreError>(
            new SessionState(currentDevice, bindings.ToImmutable())
        );
    }

    /// <inheritdoc/>
    public async Task<Result<Unit, SessionStoreError>> SaveAsync(
        SessionState state,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();

        var dto = new SessionStateDto
        {
            CurrentDevice = state.CurrentDevice?.Value,
            ActiveScenario = null, // dropped in v0.2.4; kept readable for legacy JSON only
            DeviceScenarios = state.DeviceScenarios.IsEmpty
                ? null
                : state.DeviceScenarios.ToDictionary(kv => kv.Key.Value, kv => kv.Value.Value),
        };
        var serialized = JsonSerializer.Serialize(dto, SessionJsonContext.Default.SessionStateDto);

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

        // The temp name is unique per write and the rename replaces the
        // destination in one step, so concurrent writers (e.g. two gateway
        // processes activating the same env scenario at startup) can never
        // clobber each other's temp file or leave a window with no session
        // file — a reader that finds the file missing treats it as an empty
        // session and tears down every live scenario binding.
        var tempPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await _fs.File.WriteAllTextAsync(tempPath, serialized, ct);
            ApplyUserOnlyPermissions(tempPath);
            await ReplaceWithRetryAsync(tempPath, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                _fs.File.Delete(tempPath);
            }
            catch (Exception cleanupEx)
                when (cleanupEx is IOException or UnauthorizedAccessException) { }
            return Result.Failure<Unit, SessionStoreError>(
                new SessionStoreWriteFailure($"write failed at {_path}", ex)
            );
        }

        return Result.Success<Unit, SessionStoreError>(Unit.Value);
    }

    /// <summary>
    /// Renames the written temp file onto the session path, replacing the
    /// destination in one step. On Windows a concurrent replace of the same
    /// destination transiently fails; contention between simultaneous
    /// writers (two gateway processes activating the same env scenario at
    /// startup) is legitimate, so the rename retries briefly before the
    /// failure surfaces.
    /// </summary>
    private async Task ReplaceWithRetryAsync(string tempPath, CancellationToken ct)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                _fs.File.Move(tempPath, _path, overwrite: true);
                return;
            }
            catch (Exception ex)
                when (attempt < maxAttempts && ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10 * attempt), ct);
            }
        }
    }

    /// <summary>
    /// Restricts the freshly written file to the account that wrote it, per
    /// ADR 0017 §4 — mode <c>0600</c> on Unix, a protected DACL granting only
    /// the current user on Windows. Applied to the temp file, before the
    /// rename that publishes it, so the session is never briefly readable.
    /// Best-effort: the write itself has already succeeded, and a session
    /// that cannot be locked down is better than a session that is lost.
    /// </summary>
    private void ApplyUserOnlyPermissions(string path)
    {
        // Only act when the adapter is backed by the real file system; the
        // MockFileSystem used by tests implements neither Unix modes nor ACLs.
        if (!ReferenceEquals(_fs.GetType(), typeof(FileSystem)))
        {
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                ApplyWindowsUserOnlyAcl(path);
            }
            else
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (PlatformNotSupportedException)
        {
            // No-op where the platform has no such concept.
        }
        catch (UnauthorizedAccessException)
        {
            // The file system refused the change; the content is still written.
        }
        catch (IOException)
        {
            // Best-effort; the write itself already succeeded.
        }
    }

    /// <summary>
    /// Replaces the file's inherited grants with a single explicit
    /// full-control entry for the current account. What a file inherits is a
    /// property of the directory rather than of the file: a managed
    /// workstation's profile tree can carry group grants, and
    /// <c>IVICLI_CONFIG</c> can place the session outside the profile
    /// altogether.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void ApplyWindowsUserOnlyAcl(string path)
    {
        var user = WindowsIdentity.GetCurrent().User;
        if (user is null)
        {
            return;
        }

        var file = new FileInfo(path);
        var security = file.GetAccessControl();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (
            FileSystemAccessRule rule in security.GetAccessRules(
                includeExplicit: true,
                includeInherited: false,
                typeof(SecurityIdentifier)
            )
        )
        {
            security.RemoveAccessRuleSpecific(rule);
        }
        security.AddAccessRule(
            new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow)
        );
        file.SetAccessControl(security);
    }

    internal sealed class SessionStateDto
    {
        [JsonPropertyName("current_device")]
        public string? CurrentDevice { get; set; }

        /// <summary>Legacy v0.1.x — v0.2.3 single-global-scenario field;
        /// read for migration only, never written by v0.2.4+.</summary>
        [JsonPropertyName("active_scenario")]
        public string? ActiveScenario { get; set; }

        /// <summary>Per-device scenario bindings (v0.2.4+).</summary>
        [JsonPropertyName("device_scenarios")]
        public Dictionary<string, string>? DeviceScenarios { get; set; }
    }
}

/// <summary>Source-generated serializer for the session file (issue #15).</summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(JsonSessionStore.SessionStateDto))]
internal sealed partial class SessionJsonContext : JsonSerializerContext;
