using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using IviCli.Api;
using IviCli.Api.Authentication;
using IviCli.Api.Tls;
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
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace IviCli.Api.Tests.Tls;

/// <summary>
/// End-to-end HTTPS + mTLS smoke tests for the Management API (ADR 0039).
/// Binds Kestrel on a free loopback port and round-trips an HTTP request
/// through HttpClient with a custom server-cert validator so no machine
/// trust changes are required.
/// </summary>
public sealed class TlsEndToEndTests
{
    [Fact]
    public async Task HTTPS_listener_serves_healthz_with_self_signed_cert()
    {
        using var serverCert = TlsCertificateLoader.GenerateSelfSigned();
        var bundle = new TlsCertificateBundle(serverCert, null, true);
        var port = GetFreePort();
        await using var host = await StartHostAsync(port, bundle);

        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
                cert?.Thumbprint == serverCert.Thumbprint,
        };
        using var client = new HttpClient(handler);
        var resp = await client.GetAsync($"https://localhost:{port}/healthz");
        resp.IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task mTLS_listener_accepts_client_signed_by_trusted_ca()
    {
        using var rootCa = BuildSelfSignedCa("ivi-test-root");
        using var clientCert = IssueClientCert(rootCa, "ivi-test-client-ok");
        using var serverCert = TlsCertificateLoader.GenerateSelfSigned();
        var trustBundle = new X509Certificate2Collection { rootCa };
        var bundle = new TlsCertificateBundle(serverCert, trustBundle, true);
        var port = GetFreePort();
        await using var host = await StartHostAsync(port, bundle);

        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
                cert?.Thumbprint == serverCert.Thumbprint,
            ClientCertificateOptions = ClientCertificateOption.Manual,
        };
        handler.ClientCertificates.Add(clientCert);
        using var client = new HttpClient(handler);
        var resp = await client.GetAsync($"https://localhost:{port}/healthz");
        resp.IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task mTLS_listener_rejects_client_signed_by_untrusted_ca()
    {
        using var trustedCa = BuildSelfSignedCa("ivi-test-trusted");
        using var rogueCa = BuildSelfSignedCa("ivi-test-rogue");
        using var rogueClient = IssueClientCert(rogueCa, "ivi-test-client-bad");
        using var serverCert = TlsCertificateLoader.GenerateSelfSigned();
        var trustBundle = new X509Certificate2Collection { trustedCa };
        var bundle = new TlsCertificateBundle(serverCert, trustBundle, true);
        var port = GetFreePort();
        await using var host = await StartHostAsync(port, bundle);

        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
                cert?.Thumbprint == serverCert.Thumbprint,
            ClientCertificateOptions = ClientCertificateOption.Manual,
        };
        handler.ClientCertificates.Add(rogueClient);
        using var client = new HttpClient(handler);

        var ex = await Should.ThrowAsync<HttpRequestException>(() =>
            client.GetAsync($"https://localhost:{port}/healthz")
        );
        // Either the TLS handshake breaks (most common) or Kestrel
        // accepts then rejects the request. Both surface as
        // HttpRequestException; either path proves the rogue client
        // failed to talk to /healthz.
        ex.ShouldNotBeNull();
    }

    private static async Task<Microsoft.AspNetCore.Builder.WebApplication> StartHostAsync(
        int port,
        TlsCertificateBundle bundle
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

        var app = IviCliApiBuilder.Build(sp, IPAddress.Loopback, port, bundle);
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

    private static X509Certificate2 BuildSelfSignedCa(string cn)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            $"CN={cn}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                true
            )
        );
        var ca = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddHours(4)
        );
        var pfx = ca.Export(X509ContentType.Pfx);
        ca.Dispose();
        return X509CertificateLoader.LoadPkcs12(pfx, password: null);
    }

    private static X509Certificate2 IssueClientCert(X509Certificate2 ca, string cn)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            $"CN={cn}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                true
            )
        );
        req.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.2") }, // clientAuth
                true
            )
        );
        var serial = new byte[16];
        Random.Shared.NextBytes(serial);
        using var issued = req.Create(
            ca,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddHours(1),
            serial
        );
        using var withKey = issued.CopyWithPrivateKey(rsa);
        var pfx = withKey.Export(X509ContentType.Pfx);
        return X509CertificateLoader.LoadPkcs12(pfx, password: null);
    }
}
