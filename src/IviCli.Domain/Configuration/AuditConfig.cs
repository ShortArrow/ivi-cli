namespace IviCli.Domain.Configuration;

/// <summary>
/// The <c>[audit]</c> section of a configuration document (ADR 0043).
/// Controls the append-only audit log: whether it runs, and where
/// the NDJSON file lives.
/// </summary>
public sealed record AuditConfig
{
    /// <summary>
    /// Default audit configuration — enabled with a null path,
    /// meaning the composition root resolves the canonical
    /// <c>${IVICLI_DATA_DIR}/audit/audit.ndjson</c> location.
    /// </summary>
    public static AuditConfig Default { get; } = new(enabled: true, path: null);

    /// <summary>When <see langword="false"/> the composition root binds <c>NullAuditLog</c>.</summary>
    public bool Enabled { get; }

    /// <summary>Optional override of the NDJSON file path. Null = derived from IviPaths.</summary>
    public string? Path { get; }

    private AuditConfig(bool enabled, string? path)
    {
        Enabled = enabled;
        Path = path;
    }

    /// <summary>Validates and constructs an <see cref="AuditConfig"/>.</summary>
    public static Result<AuditConfig, AuditConfigError> From(bool enabled, string? path)
    {
        if (path is not null && string.IsNullOrWhiteSpace(path))
        {
            return Result.Failure<AuditConfig, AuditConfigError>(new AuditPathBlank());
        }
        return Result.Success<AuditConfig, AuditConfigError>(
            new AuditConfig(enabled, string.IsNullOrWhiteSpace(path) ? null : path)
        );
    }
}

/// <summary>Errors that can surface from <see cref="AuditConfig.From"/>.</summary>
public abstract record AuditConfigError : IviError
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

/// <summary>Audit path was present but blank (whitespace) — operator likely meant to omit it.</summary>
public sealed record AuditPathBlank : AuditConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message =>
        "[audit].path must not be blank — omit it to use the default location";
}
