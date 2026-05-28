using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using IviCli.Domain;
using IviCli.Domain.Configuration;

namespace IviCli.Api.Tls;

/// <summary>
/// Resolves the runtime certificate material from a validated
/// <see cref="TlsConfig"/> (ADR 0039). Loads PFX / PEM cert+key from
/// disk, generates a self-signed dev cert for <c>--tls-self-signed</c>,
/// and parses a PEM bundle of trusted client-cert CAs for mTLS.
/// </summary>
public static class TlsCertificateLoader
{
    /// <summary>
    /// Loads the server certificate and (optionally) the client-CA bundle
    /// described by <paramref name="config"/>. Returns
    /// <see cref="TlsLoadError"/> when the cert files are missing,
    /// malformed, or the PFX password is empty.
    /// </summary>
    public static Result<TlsCertificateBundle, TlsLoadError> Load(TlsConfig config)
    {
        if (!config.Enabled)
        {
            return Result.Failure<TlsCertificateBundle, TlsLoadError>(new TlsLoadDisabled());
        }

        var serverCertResult = ResolveServerCertificate(config);
        if (serverCertResult is not Result<X509Certificate2, TlsLoadError>.Ok serverOk)
        {
            return Result.Failure<TlsCertificateBundle, TlsLoadError>(
                ((Result<X509Certificate2, TlsLoadError>.Error)serverCertResult).Err
            );
        }

        X509Certificate2Collection? clientCas = null;
        if (config.ClientRequired)
        {
            var caResult = ResolveClientCaBundle(config.ClientCaPath!);
            if (caResult is not Result<X509Certificate2Collection, TlsLoadError>.Ok caOk)
            {
                return Result.Failure<TlsCertificateBundle, TlsLoadError>(
                    ((Result<X509Certificate2Collection, TlsLoadError>.Error)caResult).Err
                );
            }
            clientCas = caOk.Value;
        }

        return Result.Success<TlsCertificateBundle, TlsLoadError>(
            new TlsCertificateBundle(serverOk.Value, clientCas, config.SelfSigned)
        );
    }

    private static Result<X509Certificate2, TlsLoadError> ResolveServerCertificate(TlsConfig config)
    {
        if (config.SelfSigned)
        {
            return Result.Success<X509Certificate2, TlsLoadError>(GenerateSelfSigned());
        }

        var path = config.CertPath!;
        if (!File.Exists(path))
        {
            return Result.Failure<X509Certificate2, TlsLoadError>(new TlsCertFileMissing(path));
        }

        var passwordEnv = config.PasswordEnv;
        var password = passwordEnv is null ? null : Environment.GetEnvironmentVariable(passwordEnv);

        try
        {
            // PEM cert + separate PEM key.
            if (config.KeyPath is { } keyPath)
            {
                if (!File.Exists(keyPath))
                {
                    return Result.Failure<X509Certificate2, TlsLoadError>(
                        new TlsCertFileMissing(keyPath)
                    );
                }
                var pem = X509Certificate2.CreateFromPemFile(path, keyPath);
                // CreateFromPemFile returns an ephemeral cert; export+reimport
                // so Kestrel can use the private key cross-platform.
                var pfxBytes = pem.Export(X509ContentType.Pfx);
                return Result.Success<X509Certificate2, TlsLoadError>(
                    X509CertificateLoader.LoadPkcs12(pfxBytes, password: null)
                );
            }
            // Single-file PFX.
            return Result.Success<X509Certificate2, TlsLoadError>(
                X509CertificateLoader.LoadPkcs12FromFile(path, password)
            );
        }
        catch (Exception ex)
        {
            return Result.Failure<X509Certificate2, TlsLoadError>(
                new TlsCertLoadFailure(path, ex.Message, ex)
            );
        }
    }

    private static Result<X509Certificate2Collection, TlsLoadError> ResolveClientCaBundle(
        string path
    )
    {
        if (!File.Exists(path))
        {
            return Result.Failure<X509Certificate2Collection, TlsLoadError>(
                new TlsCertFileMissing(path)
            );
        }
        try
        {
            var pem = File.ReadAllText(path);
            var collection = new X509Certificate2Collection();
            collection.ImportFromPem(pem);
            if (collection.Count == 0)
            {
                return Result.Failure<X509Certificate2Collection, TlsLoadError>(
                    new TlsCertLoadFailure(path, "no certificates found in PEM bundle", null)
                );
            }
            return Result.Success<X509Certificate2Collection, TlsLoadError>(collection);
        }
        catch (Exception ex)
        {
            return Result.Failure<X509Certificate2Collection, TlsLoadError>(
                new TlsCertLoadFailure(path, ex.Message, ex)
            );
        }
    }

    /// <summary>
    /// Generates an ephemeral self-signed certificate for development use.
    /// The cert covers <c>localhost</c>, <c>127.0.0.1</c>, and <c>::1</c>
    /// and is valid for 24 hours so abuse stays visible.
    /// </summary>
    public static X509Certificate2 GenerateSelfSigned()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=ivi-cli-dev",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        san.AddIpAddress(IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(san.Build());

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: false, false, 0, true)
        );
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true
            )
        );
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, // serverAuth
                critical: true
            )
        );

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = notBefore.AddHours(24);
        var cert = request.CreateSelfSigned(notBefore, notAfter);
        // Round-trip through PFX so the private key is portable across
        // platforms (matches NI/Keysight handling on Linux runners).
        var pfx = cert.Export(X509ContentType.Pfx);
        cert.Dispose();
        return X509CertificateLoader.LoadPkcs12(pfx, password: null);
    }
}

/// <summary>The resolved TLS material consumed by the Kestrel HTTPS configuration.</summary>
/// <param name="ServerCertificate">Server cert presented to clients.</param>
/// <param name="ClientCaBundle">Trusted client CA certs when mTLS is enabled.</param>
/// <param name="SelfSigned">True when the cert was generated at startup; logged at Warning so operators don't deploy with it.</param>
public sealed record TlsCertificateBundle(
    X509Certificate2 ServerCertificate,
    X509Certificate2Collection? ClientCaBundle,
    bool SelfSigned
);

/// <summary>Failures emitted by <see cref="TlsCertificateLoader.Load"/>.</summary>
public abstract record TlsLoadError : IviError
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

/// <summary>The loader was called on a TLS-disabled config (programmer error — always pre-check <see cref="TlsConfig.Enabled"/>).</summary>
public sealed record TlsLoadDisabled : TlsLoadError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Critical;

    /// <inheritdoc/>
    public override string Message => "TlsCertificateLoader.Load called with TLS disabled";
}

/// <summary>The cert / key / CA file at the supplied path is missing.</summary>
public sealed record TlsCertFileMissing(string Path) : TlsLoadError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Error;

    /// <inheritdoc/>
    public override string Message => "TLS certificate file not found: {Path}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Path };
}

/// <summary>The certificate file existed but could not be parsed.</summary>
public sealed record TlsCertLoadFailure(string Path, string Reason, Exception? InnerException)
    : TlsLoadError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Error;

    /// <inheritdoc/>
    public override string Message => "TLS certificate at {Path} could not be loaded: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Path, Reason };

    /// <inheritdoc/>
    public override Exception? Cause => InnerException;
}
