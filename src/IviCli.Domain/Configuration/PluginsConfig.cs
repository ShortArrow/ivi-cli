using System.Collections.Immutable;

namespace IviCli.Domain.Configuration;

/// <summary>
/// The <c>[plugins]</c> section of a configuration document (ADR 0013).
/// Plugins are opt-in for security: the host loads nothing unless
/// <see cref="Enabled"/> is true. An optional <see cref="Allowed"/>
/// list further constrains which plugin names the loader honours.
/// </summary>
public sealed record PluginsConfig
{
    /// <summary>The plugins-disabled default — no plugin DLL is loaded.</summary>
    public static PluginsConfig Default { get; } =
        new(enabled: false, allowed: ImmutableArray<string>.Empty);

    /// <summary>When <see langword="false"/> the loader is skipped entirely.</summary>
    public bool Enabled { get; }

    /// <summary>
    /// Optional allowlist of plugin names the loader honours. Empty
    /// = no allowlist (every discovered plugin loads when
    /// <see cref="Enabled"/>).
    /// </summary>
    public ImmutableArray<string> Allowed { get; }

    private PluginsConfig(bool enabled, ImmutableArray<string> allowed)
    {
        Enabled = enabled;
        Allowed = allowed;
    }

    /// <summary>Validates and constructs a <see cref="PluginsConfig"/>.</summary>
    public static Result<PluginsConfig, PluginsConfigError> From(
        bool enabled,
        ImmutableArray<string> allowed
    )
    {
        foreach (var name in allowed)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Result.Failure<PluginsConfig, PluginsConfigError>(
                    new PluginsAllowedEntryBlank()
                );
            }
        }
        return Result.Success<PluginsConfig, PluginsConfigError>(
            new PluginsConfig(enabled, allowed)
        );
    }

    /// <summary>True when <paramref name="name"/> is permitted by this config.</summary>
    public bool IsAllowed(string name) => Allowed.IsDefaultOrEmpty || Allowed.Contains(name);

    /// <inheritdoc/>
    public bool Equals(PluginsConfig? other) =>
        other is not null && Enabled == other.Enabled && Allowed.SequenceEqual(other.Allowed);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Enabled);
        foreach (var a in Allowed)
        {
            hash.Add(a);
        }
        return hash.ToHashCode();
    }
}

/// <summary>Errors that surface from <see cref="PluginsConfig.From"/>.</summary>
public abstract record PluginsConfigError : IviError
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

/// <summary>An entry in the <c>allowed</c> list was blank or whitespace.</summary>
public sealed record PluginsAllowedEntryBlank : PluginsConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "[plugins].allowed must not contain blank entries";
}
