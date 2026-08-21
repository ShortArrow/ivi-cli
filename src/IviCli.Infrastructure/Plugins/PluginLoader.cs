using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using System.Reflection;
using System.Runtime.Loader;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Plugin;
using Microsoft.Extensions.Logging;
using Tomlyn;
using Tomlyn.Model;

namespace IviCli.Infrastructure.Plugins;

/// <summary>
/// Discovers and loads ivi-cli plugins from a directory (ADR 0013).
/// Each plugin lives in its own subdirectory containing a
/// <c>plugin.toml</c> manifest plus a managed-code DLL named by
/// the manifest's <c>assembly</c> field.
/// </summary>
/// <remarks>
/// The loader uses one <see cref="AssemblyLoadContext"/> per plugin
/// so two plugins shipping conflicting versions of the same
/// transitive dependency do not collide. v1 contexts are not
/// collectible — plugin unload is a v2 follow-up.
/// </remarks>
[RequiresUnreferencedCode(
    "Plugins are loaded by reflection from arbitrary assemblies; guard call sites with PluginSupport.IsSupported."
)]
public sealed class PluginLoader
{
    private readonly IFileSystem _fs;
    private readonly ILogger<PluginLoader>? _logger;

    /// <summary>Creates a loader bound to the supplied file system.</summary>
    public PluginLoader(IFileSystem fs, ILogger<PluginLoader>? logger = null)
    {
        _fs = fs;
        _logger = logger;
    }

    /// <summary>
    /// Discovers, loads, and instantiates every plugin in
    /// <paramref name="pluginsDir"/> that is permitted by
    /// <paramref name="config"/>. Failures on individual plugins
    /// are logged at Warning and the loader continues with the
    /// remaining entries — one malformed plugin must not break
    /// the host.
    /// </summary>
    public IReadOnlyList<LoadedPlugin> LoadAll(PluginsConfig config, string pluginsDir)
    {
        if (!config.Enabled)
        {
            return Array.Empty<LoadedPlugin>();
        }
        if (!_fs.Directory.Exists(pluginsDir))
        {
            return Array.Empty<LoadedPlugin>();
        }

        var loaded = new List<LoadedPlugin>();
        foreach (var subdir in _fs.Directory.EnumerateDirectories(pluginsDir))
        {
            var result = LoadOne(config, subdir);
            if (result is Result<LoadedPlugin, PluginLoadError>.Ok ok)
            {
                loaded.Add(ok.Value);
            }
            else if (result is Result<LoadedPlugin, PluginLoadError>.Error err)
            {
                _logger?.LogWarning("skipping plugin at {Dir}: {Reason}", subdir, err.Err.Message);
            }
        }
        return loaded;
    }

    /// <summary>Discovers + loads a single plugin directory. Returns Ok or a specific failure.</summary>
    public Result<LoadedPlugin, PluginLoadError> LoadOne(PluginsConfig config, string subdir)
    {
        var manifestPath = _fs.Path.Combine(subdir, "plugin.toml");
        if (!_fs.File.Exists(manifestPath))
        {
            return Result.Failure<LoadedPlugin, PluginLoadError>(new PluginManifestMissing(subdir));
        }

        var toml = _fs.File.ReadAllText(manifestPath);
        var manifestResult = ParseManifest(toml);
        if (manifestResult is not Result<PluginManifest, PluginLoadError>.Ok manifestOk)
        {
            return Result.Failure<LoadedPlugin, PluginLoadError>(
                ((Result<PluginManifest, PluginLoadError>.Error)manifestResult).Err
            );
        }
        var manifest = manifestOk.Value;

        if (manifest.TargetApiVersion != HostApiVersion.Current)
        {
            return Result.Failure<LoadedPlugin, PluginLoadError>(
                new PluginApiVersionMismatch(
                    manifest.Name,
                    manifest.TargetApiVersion,
                    HostApiVersion.Current
                )
            );
        }

        if (!config.IsAllowed(manifest.Name))
        {
            return Result.Failure<LoadedPlugin, PluginLoadError>(
                new PluginNotAllowed(manifest.Name)
            );
        }

        var dllPath = _fs.Path.Combine(subdir, manifest.Assembly);
        if (!_fs.File.Exists(dllPath))
        {
            return Result.Failure<LoadedPlugin, PluginLoadError>(
                new PluginAssemblyMissing(manifest.Name, dllPath)
            );
        }

        Assembly assembly;
        try
        {
            // AssemblyLoadContext operates against the real file system —
            // ivi-cli's IFileSystem abstraction is for tests that don't
            // actually need to bind native code. Loading happens lazily
            // on the first member access; v1 forces it by enumerating
            // exported types below.
            var context = new AssemblyLoadContext(manifest.Name, isCollectible: false);
            assembly = context.LoadFromAssemblyPath(dllPath);
        }
        catch (Exception ex)
        {
            return Result.Failure<LoadedPlugin, PluginLoadError>(
                new PluginAssemblyLoadFailure(manifest.Name, ex.Message, ex)
            );
        }

        Type? entryType;
        try
        {
            entryType = assembly.GetType(manifest.EntryPoint, throwOnError: false);
        }
        catch (Exception ex)
        {
            return Result.Failure<LoadedPlugin, PluginLoadError>(
                new PluginEntryPointNotFound(manifest.Name, manifest.EntryPoint, ex.Message)
            );
        }
        if (entryType is null)
        {
            return Result.Failure<LoadedPlugin, PluginLoadError>(
                new PluginEntryPointNotFound(
                    manifest.Name,
                    manifest.EntryPoint,
                    "type not present in assembly"
                )
            );
        }
        if (!typeof(IIviPlugin).IsAssignableFrom(entryType))
        {
            return Result.Failure<LoadedPlugin, PluginLoadError>(
                new PluginEntryPointNotIIviPlugin(manifest.Name, manifest.EntryPoint)
            );
        }

        IIviPlugin instance;
        try
        {
            instance = (IIviPlugin)Activator.CreateInstance(entryType)!;
        }
        catch (Exception ex)
        {
            return Result.Failure<LoadedPlugin, PluginLoadError>(
                new PluginInstantiationFailure(manifest.Name, ex.Message, ex)
            );
        }

        return Result.Success<LoadedPlugin, PluginLoadError>(new LoadedPlugin(manifest, instance));
    }

    private static Result<PluginManifest, PluginLoadError> ParseManifest(string toml)
    {
        TomlTable model;
        try
        {
            model =
                TomlSerializer.Deserialize<TomlTable>(toml, TomlModelContext.Default)
                ?? new TomlTable();
        }
        catch (TomlException ex)
        {
            return Result.Failure<PluginManifest, PluginLoadError>(
                new PluginManifestSyntaxError(ex.Message)
            );
        }
        if (!model.TryGetValue("plugin", out var pluginValue) || pluginValue is not TomlTable t)
        {
            return Result.Failure<PluginManifest, PluginLoadError>(
                new PluginManifestSyntaxError("expected [plugin] table")
            );
        }
        var name = ReadString(t, "name") ?? "";
        var version = ReadString(t, "version") ?? "";
        var entryPoint = ReadString(t, "entry_point") ?? "";
        var assembly = ReadString(t, "assembly") ?? "";
        var apiVersion = ReadLong(t, "target_api_version", 0);

        var built = PluginManifest.From(name, version, (int)apiVersion, entryPoint, assembly);
        if (built is not Result<PluginManifest, PluginManifestError>.Ok ok)
        {
            var err = ((Result<PluginManifest, PluginManifestError>.Error)built).Err;
            return Result.Failure<PluginManifest, PluginLoadError>(
                new PluginManifestSyntaxError(err.Message)
            );
        }
        return Result.Success<PluginManifest, PluginLoadError>(ok.Value);
    }

    private static string? ReadString(TomlTable t, string key) =>
        t.TryGetValue(key, out var v) && v is string s ? s : null;

    private static long ReadLong(TomlTable t, string key, long fallback) =>
        t.TryGetValue(key, out var v) && v is long l ? l : fallback;
}

/// <summary>A successfully discovered + instantiated plugin.</summary>
public sealed record LoadedPlugin(PluginManifest Manifest, IIviPlugin Instance);

/// <summary>Failures surfaced by <see cref="PluginLoader"/>.</summary>
public abstract record PluginLoadError : IviError
{
    /// <inheritdoc/>
    public abstract LogSeverity Severity { get; }

    /// <inheritdoc/>
    public abstract string Message { get; }

    /// <inheritdoc/>
    public virtual IReadOnlyList<object?> LogArgs => Array.Empty<object?>();

    /// <inheritdoc/>
    public virtual Exception? Cause => null;
}

/// <summary>The plugin directory was missing the <c>plugin.toml</c> file.</summary>
public sealed record PluginManifestMissing(string Directory) : PluginLoadError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "plugin manifest missing in {Directory}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Directory };
}

/// <summary>The <c>plugin.toml</c> could not be parsed.</summary>
public sealed record PluginManifestSyntaxError(string Reason) : PluginLoadError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "plugin manifest syntax error: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Reason };
}

/// <summary>The plugin's target API version did not match the running host.</summary>
public sealed record PluginApiVersionMismatch(string Name, int Plugin, int Host) : PluginLoadError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message =>
        "plugin {Name} targets API v{Plugin} but host expects v{Host}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name, Plugin, Host };
}

/// <summary>The plugin name was not in the operator's allowlist.</summary>
public sealed record PluginNotAllowed(string Name) : PluginLoadError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "plugin {Name} is not in [plugins].allowed";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>The DLL named by the manifest was missing.</summary>
public sealed record PluginAssemblyMissing(string Name, string DllPath) : PluginLoadError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "plugin {Name} assembly file missing at {DllPath}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name, DllPath };
}

/// <summary>Loading the assembly into the AssemblyLoadContext threw.</summary>
public sealed record PluginAssemblyLoadFailure(
    string Name,
    string Reason,
    Exception? InnerException
) : PluginLoadError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "plugin {Name} assembly load failed: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name, Reason };

    /// <inheritdoc/>
    public override Exception? Cause => InnerException;
}

/// <summary>The manifest's <c>entry_point</c> type wasn't found in the loaded assembly.</summary>
public sealed record PluginEntryPointNotFound(string Name, string EntryPoint, string Reason)
    : PluginLoadError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message =>
        "plugin {Name} entry point {EntryPoint} could not be resolved: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name, EntryPoint, Reason };
}

/// <summary>The entry-point type does not implement <see cref="IIviPlugin"/>.</summary>
public sealed record PluginEntryPointNotIIviPlugin(string Name, string EntryPoint) : PluginLoadError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message =>
        "plugin {Name} entry point {EntryPoint} does not implement IIviPlugin";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name, EntryPoint };
}

/// <summary><c>Activator.CreateInstance</c> on the entry-point type threw.</summary>
public sealed record PluginInstantiationFailure(
    string Name,
    string Reason,
    Exception? InnerException
) : PluginLoadError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "plugin {Name} instantiation failed: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name, Reason };

    /// <inheritdoc/>
    public override Exception? Cause => InnerException;
}
