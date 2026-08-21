using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using IviCli.Api.Authentication;
using IviCli.Api.Tls;
using IviCli.Application.Audit;
using IviCli.Application.Auth;
using IviCli.Application.Backends;
using IviCli.Application.Configuration;
using IviCli.Application.Devices;
using IviCli.Application.Mock;
using IviCli.Application.Servers;
using IviCli.Application.Session;
using IviCli.Backends.Fake;
using IviCli.Domain.Configuration;
using IviCli.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace IviCli.Api.Tests.Tls;

/// <summary>
/// TLS certificate hot-reload (ADR 0039 / issue #16): a rotated cert file
/// is served on the next handshake without restarting the listener, and a
/// rotation that does not validate leaves the old certificate active.
/// </summary>
public sealed class TlsHotReloadTests
{
    private sealed class RecordingAuditLog : IAuditLog
    {
        public List<AuditEvent> Events { get; } = new();

        public Task AppendAsync(AuditEvent ev, CancellationToken ct)
        {
            lock (Events)
            {
                Events.Add(ev);
            }
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Rotation_swaps_the_served_certificate_without_restart()
    {
        var cert1 = NewPfx();
        var cert2 = NewPfx();
        var certPath = WritePfx(cert1.Pfx);
        try
        {
            var config = FileTlsConfig(certPath);
            var initial = TlsCertificateLoader.Load(config).ShouldBeOk();
            var rotation = new RotatingTlsCertificate(
                initial,
                config,
                NullLogger.Instance,
                NullAuditLog.Instance
            );
            var port = GetFreePort();
            await using var host = await StartHostAsync(port, initial, () => rotation.Current);

            (await ServedThumbprintAsync(port)).ShouldBe(cert1.Thumbprint);

            File.WriteAllBytes(certPath, cert2.Pfx);
            await PollUntilAsync(
                rotation,
                () => rotation.Current.ServerCertificate.Thumbprint == cert2.Thumbprint
            );

            (await ServedThumbprintAsync(port)).ShouldBe(cert2.Thumbprint);
        }
        finally
        {
            File.Delete(certPath);
        }
    }

    [Fact]
    public async Task A_rotation_that_does_not_parse_keeps_the_old_certificate()
    {
        var cert1 = NewPfx();
        var certPath = WritePfx(cert1.Pfx);
        try
        {
            var config = FileTlsConfig(certPath);
            var initial = TlsCertificateLoader.Load(config).ShouldBeOk();
            var rotation = new RotatingTlsCertificate(
                initial,
                config,
                NullLogger.Instance,
                NullAuditLog.Instance
            );

            File.WriteAllBytes(certPath, [0xDE, 0xAD, 0xBE, 0xEF]);
            await rotation.PollOnceAsync(CancellationToken.None);

            rotation.Current.ServerCertificate.Thumbprint.ShouldBe(cert1.Thumbprint);
        }
        finally
        {
            File.Delete(certPath);
        }
    }

    [Fact]
    public async Task An_expired_rotation_keeps_the_old_certificate()
    {
        var cert1 = NewPfx();
        var expired = NewPfx(
            notBefore: DateTimeOffset.UtcNow.AddDays(-2),
            notAfter: DateTimeOffset.UtcNow.AddDays(-1)
        );
        var certPath = WritePfx(cert1.Pfx);
        try
        {
            var config = FileTlsConfig(certPath);
            var initial = TlsCertificateLoader.Load(config).ShouldBeOk();
            var rotation = new RotatingTlsCertificate(
                initial,
                config,
                NullLogger.Instance,
                NullAuditLog.Instance
            );

            File.WriteAllBytes(certPath, expired.Pfx);
            await rotation.PollOnceAsync(CancellationToken.None);

            rotation.Current.ServerCertificate.Thumbprint.ShouldBe(cert1.Thumbprint);
        }
        finally
        {
            File.Delete(certPath);
        }
    }

    [Fact]
    public async Task A_successful_rotation_is_audited()
    {
        var cert1 = NewPfx();
        var cert2 = NewPfx();
        var certPath = WritePfx(cert1.Pfx);
        try
        {
            var config = FileTlsConfig(certPath);
            var initial = TlsCertificateLoader.Load(config).ShouldBeOk();
            var audit = new RecordingAuditLog();
            var rotation = new RotatingTlsCertificate(initial, config, NullLogger.Instance, audit);

            File.WriteAllBytes(certPath, cert2.Pfx);
            await PollUntilAsync(rotation, () => audit.Events.Count > 0);

            var lifecycle = audit.Events.ShouldHaveSingleItem().ShouldBeOfType<ServerLifecycle>();
            lifecycle.Action.ShouldBe("cert-reloaded");
            lifecycle.Server.ShouldBe("ivi-management-api");
        }
        finally
        {
            File.Delete(certPath);
        }
    }

    [Fact]
    public async Task A_transiently_unreadable_rotation_is_retried_on_the_next_tick()
    {
        if (!OperatingSystem.IsWindows())
        {
            // FileShare.None blocks readers only on Windows; the failure
            // this pins (a scanner holding the fresh PFX, seen on the
            // win-arm64 release runner for v0.3.0) is Windows-shaped too.
            return;
        }
        var cert1 = NewPfx();
        var cert2 = NewPfx();
        var certPath = WritePfx(cert1.Pfx);
        try
        {
            var config = FileTlsConfig(certPath);
            var initial = TlsCertificateLoader.Load(config).ShouldBeOk();
            var rotation = new RotatingTlsCertificate(
                initial,
                config,
                NullLogger.Instance,
                NullAuditLog.Instance
            );

            File.WriteAllBytes(certPath, cert2.Pfx);
            using (File.Open(certPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                await rotation.PollOnceAsync(CancellationToken.None);
                rotation.Current.ServerCertificate.Thumbprint.ShouldBe(cert1.Thumbprint);
            }

            // No further write happens — the retry must come from the
            // rotation itself remembering that the change is still pending.
            await rotation.PollOnceAsync(CancellationToken.None);
            rotation.Current.ServerCertificate.Thumbprint.ShouldBe(cert2.Thumbprint);
        }
        finally
        {
            File.Delete(certPath);
        }
    }

    [Fact]
    public void A_self_signed_listener_has_nothing_to_rotate()
    {
        var config = TlsConfig
            .From(
                enabled: true,
                certPath: null,
                keyPath: null,
                passwordEnv: null,
                selfSigned: true,
                clientRequired: false,
                clientCaPath: null
            )
            .ShouldBeOk();
        var initial = TlsCertificateLoader.Load(config).ShouldBeOk();
        var rotation = new RotatingTlsCertificate(
            initial,
            config,
            NullLogger.Instance,
            NullAuditLog.Instance
        );

        rotation.CanRotate.ShouldBeFalse();
    }

    /// <summary>
    /// Polls until <paramref name="done"/> holds. A single tick is not
    /// enough on CI runners whose virus scanner briefly holds the freshly
    /// written PFX — the rotation retries a failed load on the next tick
    /// by design, so the test drives ticks until it heals.
    /// </summary>
    private static async Task PollUntilAsync(RotatingTlsCertificate rotation, Func<bool> done)
    {
        for (var i = 0; i < 100 && !done(); i++)
        {
            await rotation.PollOnceAsync(CancellationToken.None);
            if (done())
            {
                return;
            }
            await Task.Delay(50);
        }
        done().ShouldBeTrue("the rotation never applied within patience");
    }

    private static TlsConfig FileTlsConfig(string certPath) =>
        TlsConfig
            .From(
                enabled: true,
                certPath: certPath,
                keyPath: null,
                passwordEnv: null,
                selfSigned: false,
                clientRequired: false,
                clientCaPath: null
            )
            .ShouldBeOk();

    /// <summary>
    /// A fresh self-signed cert as PFX bytes plus its thumbprint. The PFX
    /// is exported straight off <c>CreateSelfSigned</c> — a cert
    /// re-imported through <c>X509CertificateLoader</c> carries an
    /// ephemeral key Windows refuses to re-export.
    /// </summary>
    private static (byte[] Pfx, string Thumbprint) NewPfx(
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null
    )
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=ivi-cli-rotate-test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        using var cert = request.CreateSelfSigned(
            notBefore ?? DateTimeOffset.UtcNow.AddMinutes(-5),
            notAfter ?? DateTimeOffset.UtcNow.AddHours(1)
        );
        return (cert.Export(X509ContentType.Pfx), cert.Thumbprint);
    }

    private static string WritePfx(byte[] pfx)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "ivi-tls-rotate-" + Guid.NewGuid().ToString("N") + ".pfx"
        );
        File.WriteAllBytes(path, pfx);
        return path;
    }

    private static async Task<string?> ServedThumbprintAsync(int port)
    {
        string? served = null;
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
            {
                served = cert?.Thumbprint;
                return true;
            },
        };
        using var client = new HttpClient(handler);
        var resp = await client.GetAsync($"https://localhost:{port}/healthz");
        resp.IsSuccessStatusCode.ShouldBeTrue();
        return served;
    }

    private static async Task<Microsoft.AspNetCore.Builder.WebApplication> StartHostAsync(
        int port,
        TlsCertificateBundle bundle,
        Func<TlsCertificateBundle> tlsSource
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfigStore>(new FakeConfigStore(ConfigDocument.Empty));
        services.AddSingleton<ISessionStore>(new FakeSessionStore());
        services.AddSingleton<IScenarioStore>(
            new FakeScenarioStore(Array.Empty<Domain.Mock.MockScenario>())
        );
        services.AddSingleton<IApiTokenStore>(new FakeApiTokenStore());
        services.AddSingleton(
            new ApiAuthenticationOptions { IsLoopback = true, AllowAnonymous = true }
        );
        var fake = new FakeBackend();
        services.AddSingleton(fake);
        services.AddSingleton<IBackendFactory>(new FakeBackendFactory(fake));
        services.AddSingleton<IDeviceStatusProbe, DefaultDeviceStatusProbe>();
        services.AddSingleton<ListDevicesQueryHandler>();
        services.AddSingleton<StatusDeviceCommandHandler>();
        services.AddSingleton<ListServersQueryHandler>();
        services.AddSingleton<ListScenariosQueryHandler>();
        services.AddSingleton<QueryDeviceCommandHandler>();
        services.AddSingleton<WriteDeviceCommandHandler>();
        var sp = services.BuildServiceProvider();

        var app = IviCliApiBuilder.Build(sp, IPAddress.Loopback, port, bundle, tlsSource);
        await app.StartAsync();
        return app;
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
