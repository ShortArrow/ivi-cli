using IviCli.Domain;

namespace IviCli.Plugin;

/// <summary>
/// Parsed contents of a <c>plugin.toml</c> manifest sitting next to a
/// plugin's main DLL (ADR 0013). Operators or vendor build pipelines
/// produce this file so the host can refuse a plugin that doesn't
/// declare what it is before any code from it runs.
/// </summary>
public sealed record PluginManifest
{
    /// <summary>Stable plugin identifier (matches the directory name).</summary>
    public string Name { get; }

    /// <summary>Plugin version string (semver recommended, not enforced).</summary>
    public string Version { get; }

    /// <summary>Targeted host plugin API version (must equal <see cref="HostApiVersion.Current"/>).</summary>
    public int TargetApiVersion { get; }

    /// <summary>
    /// Fully-qualified type name of the <see cref="IIviPlugin"/>
    /// implementation inside the plugin DLL. The host instantiates
    /// it via its parameterless constructor.
    /// </summary>
    public string EntryPoint { get; }

    /// <summary>The DLL filename relative to the manifest's directory.</summary>
    public string Assembly { get; }

    private PluginManifest(
        string name,
        string version,
        int targetApiVersion,
        string entryPoint,
        string assembly
    )
    {
        Name = name;
        Version = version;
        TargetApiVersion = targetApiVersion;
        EntryPoint = entryPoint;
        Assembly = assembly;
    }

    /// <summary>Validates and constructs a <see cref="PluginManifest"/>.</summary>
    public static Result<PluginManifest, PluginManifestError> From(
        string name,
        string version,
        int targetApiVersion,
        string entryPoint,
        string assembly
    )
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<PluginManifest, PluginManifestError>(
                new PluginManifestFieldBlank("name")
            );
        }
        if (string.IsNullOrWhiteSpace(version))
        {
            return Result.Failure<PluginManifest, PluginManifestError>(
                new PluginManifestFieldBlank("version")
            );
        }
        if (string.IsNullOrWhiteSpace(entryPoint))
        {
            return Result.Failure<PluginManifest, PluginManifestError>(
                new PluginManifestFieldBlank("entry_point")
            );
        }
        if (string.IsNullOrWhiteSpace(assembly))
        {
            return Result.Failure<PluginManifest, PluginManifestError>(
                new PluginManifestFieldBlank("assembly")
            );
        }
        if (targetApiVersion <= 0)
        {
            return Result.Failure<PluginManifest, PluginManifestError>(
                new PluginManifestInvalidApiVersion(targetApiVersion)
            );
        }
        return Result.Success<PluginManifest, PluginManifestError>(
            new PluginManifest(name, version, targetApiVersion, entryPoint, assembly)
        );
    }
}

/// <summary>Errors that surface during <see cref="PluginManifest.From"/>.</summary>
public abstract record PluginManifestError : IviError
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

/// <summary>A required manifest field was blank or absent.</summary>
public sealed record PluginManifestFieldBlank(string Field) : PluginManifestError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "plugin manifest field '{Field}' must not be blank";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Field };
}

/// <summary>The target API version was not a positive integer.</summary>
public sealed record PluginManifestInvalidApiVersion(int Value) : PluginManifestError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "plugin manifest target_api_version must be > 0, got {Value}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Value };
}
