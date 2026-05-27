using System.Net;
using IviCli.Api.Authentication;
using IviCli.Api.Routing;
using IviCli.Api.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
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
    /// application is not started — the caller owns the lifecycle.
    /// </summary>
    public static WebApplication Build(IServiceProvider hostingServices, IPAddress bind, int port)
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
                }
            );
            o.AddServerHeader = false;
        });

        var app = builder.Build();
        app.UseWebSockets();
        app.UseApiTokenAuthentication();
        app.MapOpenApi("/openapi/v1.json");
        app.MapGet("/healthz", () => Microsoft.AspNetCore.Http.Results.Json(new { status = "ok" }));
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
}
