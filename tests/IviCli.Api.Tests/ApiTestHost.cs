using System.Net.Http;
using System.Net.Http.Json;
using IviCli.Api.Authentication;
using IviCli.Api.Routing;
using IviCli.Api.WebSockets;
using IviCli.Application.Auth;
using IviCli.Application.Backends;
using IviCli.Application.Configuration;
using IviCli.Application.Devices;
using IviCli.Application.Mock;
using IviCli.Application.Servers;
using IviCli.Application.Session;
using IviCli.Backends.Fake;
using IviCli.Domain.Configuration;
using IviCli.Domain.Mock;
using IviCli.TestKit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IviCli.Api.Tests;

/// <summary>
/// Spins up the Management API routes over <see cref="TestServer"/>
/// so tests can make real HTTP round-trips without binding a TCP
/// socket. Substitutes the production stores with the TestKit fakes
/// so behaviour is deterministic.
/// </summary>
internal sealed class ApiTestHost : IAsyncDisposable
{
    private readonly IHost _host;

    public ApiTestHost(IHost host, HttpClient client)
    {
        _host = host;
        Client = client;
    }

    public HttpClient Client { get; }

    /// <summary>
    /// Returns the underlying <see cref="TestServer"/> so tests can open
    /// WebSocket connections via <see cref="TestServer.CreateWebSocketClient"/>.
    /// </summary>
    public TestServer Server => _host.GetTestServer();

    public static async Task<ApiTestHost> StartAsync(
        ConfigDocument config,
        FakeBackend? backend = null,
        IEnumerable<MockScenario>? scenarios = null,
        FakeApiTokenStore? tokenStore = null,
        ApiAuthenticationOptions? authOptions = null,
        PoolConfig? poolConfig = null
    )
    {
        var resolvedAuthOptions =
            authOptions
            ?? new ApiAuthenticationOptions { IsLoopback = true, AllowAnonymous = true };
        var hostBuilder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<IConfigStore>(new FakeConfigStore(config));
                services.AddSingleton<ISessionStore>(new FakeSessionStore());
                services.AddSingleton<IScenarioStore>(new FakeScenarioStore(scenarios ?? []));
                services.AddSingleton<IApiTokenStore>(tokenStore ?? new FakeApiTokenStore());
                services.AddSingleton(resolvedAuthOptions);
                var fake = backend ?? new FakeBackend();
                services.AddSingleton(fake);
                IBackendFactory baseFactory = new FakeBackendFactory(fake);
                if (poolConfig is { Enabled: true })
                {
                    baseFactory = new PoolingBackendFactory(
                        baseFactory,
                        poolConfig,
                        TimeProvider.System
                    );
                }
                services.AddSingleton<IBackendFactory>(baseFactory);
                services.AddSingleton<IDeviceStatusProbe, DefaultDeviceStatusProbe>();
                services.AddSingleton<ListDevicesQueryHandler>();
                services.AddSingleton<StatusDeviceCommandHandler>();
                services.AddSingleton<ListServersQueryHandler>();
                services.AddSingleton<ListScenariosQueryHandler>();
                services.AddSingleton<QueryDeviceCommandHandler>();
                services.AddSingleton<WriteDeviceCommandHandler>();
            });
            web.Configure(app =>
            {
                app.UseWebSockets();
                // Test pipeline: stand-alone middleware that mirrors the
                // production ApiTokenAuthentication module. Inline lambda
                // because IApplicationBuilder.Use* extensions don't expose
                // the same WebApplication-typed UseApiTokenAuthentication
                // helper outside ASP.NET Core's own hosting model.
                app.Use(
                    async (context, next) =>
                    {
                        var path = context.Request.Path.Value;
                        if (
                            string.Equals(path, "/healthz", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(
                                path,
                                "/openapi/v1.json",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            await next();
                            return;
                        }
                        var store = context.RequestServices.GetRequiredService<IApiTokenStore>();
                        var loaded = await store.LoadAsync(context.RequestAborted);
                        if (
                            loaded
                            is not IviCli.Domain.Result<
                                IviCli.Domain.Auth.ApiTokenDocument,
                                ApiTokenStoreError
                            >.Ok { Value: var document }
                        )
                        {
                            context.Response.StatusCode = 401;
                            return;
                        }
                        var opts =
                            context.RequestServices.GetRequiredService<ApiAuthenticationOptions>();
                        if (document.Tokens.IsDefaultOrEmpty)
                        {
                            if (opts.AllowAnonymous)
                            {
                                await next();
                                return;
                            }
                            await WriteUnauthorizedAsync(context);
                            return;
                        }
                        string? candidate = ExtractToken(context);
                        if (string.IsNullOrEmpty(candidate))
                        {
                            await WriteUnauthorizedAsync(context);
                            return;
                        }
                        var hash = CreateApiTokenCommandHandler.HashHex(candidate);
                        var matched = false;
                        foreach (var t in document.Tokens)
                        {
                            if (string.Equals(t.HashHex, hash, StringComparison.OrdinalIgnoreCase))
                            {
                                matched = true;
                                break;
                            }
                        }
                        if (!matched)
                        {
                            await WriteUnauthorizedAsync(context);
                            return;
                        }
                        await next();
                    }
                );
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet(
                        "/healthz",
                        () => Microsoft.AspNetCore.Http.Results.Json(new { status = "ok" })
                    );
                    endpoints.MapDevices();
                    endpoints.MapServers();
                    endpoints.MapScenarios();
                    endpoints.MapVisa();
                    endpoints.MapVisaWebSocket();
                });
            });
        });
        var host = await hostBuilder.StartAsync();
        var client = host.GetTestClient();
        return new ApiTestHost(host, client);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    private static string? ExtractToken(Microsoft.AspNetCore.Http.HttpContext context)
    {
        if (
            context.Request.Headers.TryGetValue("Authorization", out var values)
            && values.Count > 0
        )
        {
            var auth = values[0]!;
            const string bearer = "Bearer ";
            if (auth.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
            {
                return auth[bearer.Length..].Trim();
            }
        }
        if (
            context.WebSockets.IsWebSocketRequest
            && context.WebSockets.WebSocketRequestedProtocols.Count > 0
        )
        {
            foreach (var proto in context.WebSockets.WebSocketRequestedProtocols)
            {
                if (proto.StartsWith("ivi-cli-pat.", StringComparison.Ordinal))
                {
                    return proto["ivi-cli-pat.".Length..];
                }
            }
        }
        return null;
    }

    private static async Task WriteUnauthorizedAsync(Microsoft.AspNetCore.Http.HttpContext context)
    {
        context.Response.StatusCode = 401;
        context.Response.ContentType = "application/json";
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            "{\"error\":{\"code\":\"unauthorized\",\"message\":\"missing or invalid API token\"}}"
        );
        await context.Response.Body.WriteAsync(bytes);
    }
}
