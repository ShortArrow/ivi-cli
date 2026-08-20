using IviCli.Application.Audit;
using IviCli.Application.Logging;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using Microsoft.Extensions.Logging;

namespace IviCli.Api.Tls;

/// <summary>
/// Holds the certificate material Kestrel serves and re-reads it from
/// disk when the files named by <c>[api.tls]</c> change (ADR 0039).
/// Kestrel's <c>ServerCertificateSelector</c> reads <see cref="Current"/>
/// per TLS handshake, so a swap applies from the next connection without
/// restarting the listener. A rotation that fails to load or is already
/// expired is rejected: the old material stays active, a warning is
/// logged, and the next file change is tried again — which also makes a
/// half-written cert+key pair self-heal once the writer finishes.
/// </summary>
public sealed class RotatingTlsCertificate
{
    /// <summary>
    /// Poll cadence for <see cref="RunAsync"/>. Polling rather than
    /// FileSystemWatcher: rotations are rare, ACME clients replace files
    /// by rename (which watchers miss on some mounts), and a 5 s stat of
    /// up to three paths costs nothing.
    /// </summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly TlsConfig _config;
    private readonly ILogger _logger;
    private readonly IAuditLog _audit;
    private readonly TimeProvider _time;
    private volatile TlsCertificateBundle _current;
    private (DateTime Cert, DateTime Key, DateTime Ca) _stamps;

    /// <summary>Wraps <paramref name="initial"/> as the served material.</summary>
    public RotatingTlsCertificate(
        TlsCertificateBundle initial,
        TlsConfig config,
        ILogger logger,
        IAuditLog audit,
        TimeProvider? time = null
    )
    {
        _current = initial;
        _config = config;
        _logger = logger;
        _audit = audit;
        _time = time ?? TimeProvider.System;
        _stamps = ReadStamps();
    }

    /// <summary>The bundle the next TLS handshake serves.</summary>
    public TlsCertificateBundle Current => _current;

    /// <summary>
    /// False when there is nothing on disk to watch — the self-signed dev
    /// certificate lives only in memory.
    /// </summary>
    public bool CanRotate => !_config.SelfSigned && _config.CertPath is not null;

    /// <summary>Polls until <paramref name="ct"/> is cancelled.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        if (!CanRotate)
        {
            return;
        }
        using var timer = new PeriodicTimer(PollInterval, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await PollOnceAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Listener shutdown.
        }
    }

    /// <summary>
    /// One poll tick: reload when any watched file's timestamp moved.
    /// Public so tests rotate deterministically instead of sleeping.
    /// </summary>
    public async Task PollOnceAsync(CancellationToken ct)
    {
        if (!CanRotate)
        {
            return;
        }
        var stamps = ReadStamps();
        if (stamps == _stamps)
        {
            return;
        }
        _stamps = stamps;

        var loaded = TlsCertificateLoader.Load(_config);
        if (loaded is not Result<TlsCertificateBundle, TlsLoadError>.Ok { Value: var bundle })
        {
            _logger.LogIviError(((Result<TlsCertificateBundle, TlsLoadError>.Error)loaded).Err);
            _logger.LogWarning(
                "TLS certificate rotation rejected; keeping the previous certificate (thumbprint {Thumbprint})",
                _current.ServerCertificate.Thumbprint
            );
            return;
        }

        var now = _time.GetUtcNow();
        if (bundle.ServerCertificate.NotAfter.ToUniversalTime() <= now.UtcDateTime)
        {
            _logger.LogWarning(
                "TLS certificate rotation rejected: the new certificate expired {NotAfter:u}; keeping the previous certificate (thumbprint {Thumbprint})",
                bundle.ServerCertificate.NotAfter.ToUniversalTime(),
                _current.ServerCertificate.Thumbprint
            );
            return;
        }

        _current = bundle;
        _logger.LogInformation(
            "TLS certificate reloaded: thumbprint {Thumbprint}, expires {NotAfter:u}",
            bundle.ServerCertificate.Thumbprint,
            bundle.ServerCertificate.NotAfter.ToUniversalTime()
        );
        try
        {
            await _audit.AppendAsync(
                new ServerLifecycle(now, Server: "ivi-management-api", Action: "cert-reloaded"),
                ct
            );
        }
        catch
        {
            // Audit failures must not break the listener (ADR 0043).
        }
    }

    private (DateTime Cert, DateTime Key, DateTime Ca) ReadStamps() =>
        (Stamp(_config.CertPath), Stamp(_config.KeyPath), Stamp(_config.ClientCaPath));

    private static DateTime Stamp(string? path) =>
        path is null ? default : File.GetLastWriteTimeUtc(path);
}
