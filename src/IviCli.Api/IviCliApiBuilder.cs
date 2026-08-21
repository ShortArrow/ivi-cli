using System.Net;
using IviCli.Api.Authentication;
using IviCli.Api.Routing;
using IviCli.Api.Tls;
using IviCli.Api.WebSockets;
using IviCli.Domain.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IviCli.Api;

/// <summary>
/// Embeds the Management API (ADR 0034) inside an existing CLI process.
/// Returns a configured <see cref="WebApplication"/> ready for the caller
/// to invoke <c>RunAsync</c> on. The CLI's already-built
/// <see cref="IServiceProvider"/> is supplied so the API reuses every
/// Application handler the CLI already wired.
/// </summary>
public static class IviCliApiBuilder
{
    /// <summary>
    /// Configures and returns a Kestrel-hosted minimal-API application
    /// listening on <paramref name="bind"/>:<paramref name="port"/>. The
    /// application is not started — the caller owns the lifecycle. When
    /// <paramref name="tls"/> is supplied, the listener serves HTTPS
    /// (and optionally enforces mTLS) per ADR 0039.
    /// </summary>
    public static WebApplication Build(
        IServiceProvider hostingServices,
        IPAddress bind,
        int port,
        TlsCertificateBundle? tls = null,
        Func<TlsCertificateBundle>? tlsSource = null
    )
    {
        var builder = WebApplication.CreateSlimBuilder();
        // Replace the slim builder's default DI with the CLI's container so
        // every Application handler is resolvable from minimal-API handlers.
        builder.Services.AddRouting();
        builder.Services.AddOpenApi();
        // Forward singleton handler resolutions to the parent provider so the
        // CLI and the API share state (session store, scenario store, ...).
        foreach (var sd in EnumerateForwardableServices(hostingServices))
        {
            builder.Services.Add(sd);
        }

        builder.WebHost.ConfigureKestrel(o =>
        {
            o.Listen(
                bind,
                port,
                lo =>
                {
                    lo.Protocols = HttpProtocols.Http1;
                    if (tls is not null)
                    {
                        // Read through the source per handshake so a hot
                        // reload (RotatingTlsCertificate, ADR 0039) applies
                        // to the next connection without a restart.
                        var source = tlsSource ?? (() => tls);
                        lo.UseHttps(httpsOptions =>
                        {
                            httpsOptions.ServerCertificateSelector = (_, _) =>
                                source().ServerCertificate;
                            if (tls.ClientCaBundle is not null)
                            {
                                httpsOptions.ClientCertificateMode =
                                    ClientCertificateMode.RequireCertificate;
                                httpsOptions.ClientCertificateValidation = (cert, chain, _) =>
                                    chain is not null
                                    && source().ClientCaBundle is { } caBundle
                                    && ValidateClientChain(cert, chain, caBundle);
                            }
                        });
                    }
                }
            );
            o.AddServerHeader = false;
        });

        var app = builder.Build();
        app.UseWebSockets();
        // Audit middleware sits OUTSIDE the auth middleware so successful
        // PAT validations on the inside still get recorded as ApiRequest
        // events here (ADR 0043). Auth-level events (success/fail) come
        // from the inner middleware directly.
        app.Use(
            async (context, next) =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await next();
                sw.Stop();
                var audit = context.RequestServices.GetService<Application.Audit.IAuditLog>();
                if (audit is not null)
                {
                    try
                    {
                        await audit.AppendAsync(
                            new Application.Audit.ApiRequest(
                                DateTimeOffset.UtcNow,
                                Method: context.Request.Method,
                                Path: context.Request.Path.Value ?? "",
                                Status: context.Response.StatusCode,
                                Subject: null,
                                LatencyMs: (int)sw.Elapsed.TotalMilliseconds
                            ),
                            CancellationToken.None
                        );
                    }
                    catch
                    { /* audit failures must not break the request */
                    }
                }
            }
        );
        app.UseApiTokenAuthentication();
        app.MapOpenApi("/openapi/v1.json");
        app.MapGet(
            "/healthz",
            () =>
                Microsoft.AspNetCore.Http.Results.Json(
                    new HealthzDto("ok"),
                    ApiJsonContext.Default.HealthzDto
                )
        );
        app.MapDevices();
        app.MapServers();
        app.MapScenarios();
        app.MapVisa();
        app.MapVisaWebSocket();
        return app;
    }

    /// <summary>
    /// Re-registers the application's singleton handlers as factories that
    /// delegate to <paramref name="parent"/>. Only the explicit handler /
    /// store / factory types the API actually resolves are forwarded so we
    /// avoid pulling Kestrel-incompatible options out of the CLI's
    /// container.
    /// </summary>
    private static IEnumerable<ServiceDescriptor> EnumerateForwardableServices(
        IServiceProvider parent
    )
    {
        // Application handlers used by Routing/*.cs:
        yield return Forward<Application.Devices.ListDevicesQueryHandler>(parent);
        yield return Forward<Application.Devices.StatusDeviceCommandHandler>(parent);
        yield return Forward<Application.Servers.ListServersQueryHandler>(parent);
        yield return Forward<Application.Mock.ListScenariosQueryHandler>(parent);
        yield return Forward<Application.Devices.QueryDeviceCommandHandler>(parent);
        yield return Forward<Application.Devices.WriteDeviceCommandHandler>(parent);
        yield return Forward<Application.Auth.IApiTokenStore>(parent);
        // Audit log (ADR 0043) — middleware emits AuthSucceeded /
        // AuthFailed / ApiRequest through this port. When the parent
        // didn't register one (e.g. tests), use the singleton no-op.
        yield return ServiceDescriptor.Singleton<Application.Audit.IAuditLog>(_ =>
            parent.GetService<Application.Audit.IAuditLog>()
            ?? Application.Audit.NullAuditLog.Instance
        );
        // ApiAuthenticationOptions is populated by the CLI `api start` verb
        // before app.Run(); forward whatever the parent registered.
        var optsDescriptor = ServiceDescriptor.Singleton<Authentication.ApiAuthenticationOptions>(
            sp =>
                parent.GetService<Authentication.ApiAuthenticationOptions>()
                ?? new Authentication.ApiAuthenticationOptions()
        );
        yield return optsDescriptor;
    }

    private static ServiceDescriptor Forward<T>(IServiceProvider parent)
        where T : class => ServiceDescriptor.Singleton<T>(_ => parent.GetRequiredService<T>());

    /// <summary>
    /// Custom mTLS client-cert validation: re-roots the supplied chain
    /// against the configured trust bundle instead of the OS store, so
    /// operators can pin a private CA without modifying machine trust.
    /// </summary>
    private static bool ValidateClientChain(
        System.Security.Cryptography.X509Certificates.X509Certificate2 clientCert,
        System.Security.Cryptography.X509Certificates.X509Chain chain,
        System.Security.Cryptography.X509Certificates.X509Certificate2Collection trustBundle
    )
    {
        chain.ChainPolicy.TrustMode = System
            .Security
            .Cryptography
            .X509Certificates
            .X509ChainTrustMode
            .CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Clear();
        chain.ChainPolicy.CustomTrustStore.AddRange(trustBundle);
        chain.ChainPolicy.RevocationMode = System
            .Security
            .Cryptography
            .X509Certificates
            .X509RevocationMode
            .NoCheck;
        return chain.Build(clientCert);
    }
}
