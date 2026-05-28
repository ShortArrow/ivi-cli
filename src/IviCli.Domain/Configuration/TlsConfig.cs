namespace IviCli.Domain.Configuration;

/// <summary>
/// The <c>[api.tls]</c> sub-table — Kestrel HTTPS configuration for
/// the Management API listener (ADR 0039). TLS is fully opt-in;
/// <see cref="TlsConfig.Default"/> is "disabled" and the API runs
/// plaintext HTTP as it has since ADR 0034.
/// </summary>
public sealed record TlsConfig
{
    /// <summary>TLS-disabled default — plaintext HTTP listener.</summary>
    public static TlsConfig Default { get; } =
        new(
            enabled: false,
            certPath: null,
            keyPath: null,
            passwordEnv: null,
            selfSigned: false,
            clientRequired: false,
            clientCaPath: null
        );

    /// <summary>When <see langword="true"/>, the listener serves HTTPS instead of HTTP.</summary>
    public bool Enabled { get; }

    /// <summary>Absolute path to the server certificate (PFX or PEM). Mutually exclusive with <see cref="SelfSigned"/>.</summary>
    public string? CertPath { get; }

    /// <summary>Absolute path to the PEM private key. Required when <see cref="CertPath"/> is a PEM file.</summary>
    public string? KeyPath { get; }

    /// <summary>Environment variable that supplies the PFX password (read at startup).</summary>
    public string? PasswordEnv { get; }

    /// <summary>When <see langword="true"/>, the listener generates an ephemeral self-signed cert at startup (dev convenience).</summary>
    public bool SelfSigned { get; }

    /// <summary>When <see langword="true"/>, every client must present a valid certificate (mTLS).</summary>
    public bool ClientRequired { get; }

    /// <summary>Absolute path to the PEM bundle of trusted client-cert CAs. Required when <see cref="ClientRequired"/>.</summary>
    public string? ClientCaPath { get; }

    private TlsConfig(
        bool enabled,
        string? certPath,
        string? keyPath,
        string? passwordEnv,
        bool selfSigned,
        bool clientRequired,
        string? clientCaPath
    )
    {
        Enabled = enabled;
        CertPath = certPath;
        KeyPath = keyPath;
        PasswordEnv = passwordEnv;
        SelfSigned = selfSigned;
        ClientRequired = clientRequired;
        ClientCaPath = clientCaPath;
    }

    /// <summary>
    /// Validates and constructs a <see cref="TlsConfig"/>. When <see cref="Enabled"/>
    /// is <see langword="true"/> exactly one of <see cref="CertPath"/> or
    /// <see cref="SelfSigned"/> must be supplied. When <see cref="ClientRequired"/>
    /// is <see langword="true"/>, <see cref="ClientCaPath"/> must be supplied.
    /// </summary>
    public static Result<TlsConfig, TlsConfigError> From(
        bool enabled,
        string? certPath,
        string? keyPath,
        string? passwordEnv,
        bool selfSigned,
        bool clientRequired,
        string? clientCaPath
    )
    {
        if (enabled)
        {
            var hasCert = !string.IsNullOrWhiteSpace(certPath);
            if (hasCert == selfSigned)
            {
                return Result.Failure<TlsConfig, TlsConfigError>(new TlsCertSourceAmbiguous());
            }
        }
        else
        {
            // Disabled: every cert-related field must be empty to avoid a
            // misleading "set but ignored" config.
            if (
                !string.IsNullOrWhiteSpace(certPath)
                || !string.IsNullOrWhiteSpace(keyPath)
                || !string.IsNullOrWhiteSpace(passwordEnv)
                || selfSigned
                || clientRequired
                || !string.IsNullOrWhiteSpace(clientCaPath)
            )
            {
                return Result.Failure<TlsConfig, TlsConfigError>(new TlsDisabledButOptionsSet());
            }
        }
        if (clientRequired && string.IsNullOrWhiteSpace(clientCaPath))
        {
            return Result.Failure<TlsConfig, TlsConfigError>(new TlsClientCaMissing());
        }
        return Result.Success<TlsConfig, TlsConfigError>(
            new TlsConfig(
                enabled,
                NullIfEmpty(certPath),
                NullIfEmpty(keyPath),
                NullIfEmpty(passwordEnv),
                selfSigned,
                clientRequired,
                NullIfEmpty(clientCaPath)
            )
        );
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>Errors that can surface from <see cref="TlsConfig.From"/>.</summary>
public abstract record TlsConfigError : IviError
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

/// <summary>TLS is enabled but neither a cert path nor self-signed mode was supplied (or both were).</summary>
public sealed record TlsCertSourceAmbiguous : TlsConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message =>
        "exactly one of [api.tls].cert_path or [api.tls].self_signed must be set when tls is enabled";
}

/// <summary>TLS is disabled but cert-related options were supplied.</summary>
public sealed record TlsDisabledButOptionsSet : TlsConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message =>
        "tls is disabled but cert/key/client-ca options are set — clear them or enable tls";
}

/// <summary>mTLS is required but the client CA bundle path is empty.</summary>
public sealed record TlsClientCaMissing : TlsConfigError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message =>
        "[api.tls].client_required is true but client_ca_path is empty";
}
