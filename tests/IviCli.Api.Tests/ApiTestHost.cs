using System.Net.Http;
using System.Net.Http.Json;
using IviCli.Api.Routing;
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

    public static async Task<ApiTestHost> StartAsync(
        ConfigDocument config,
        FakeBackend? backend = null,
        IEnumerable<MockScenario>? scenarios = null
    )
    {
        var hostBuilder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<IConfigStore>(new FakeConfigStore(config));
                services.AddSingleton<ISessionStore>(new FakeSessionStore());
                services.AddSingleton<IScenarioStore>(new FakeScenarioStore(scenarios ?? []));
                var fake = backend ?? new FakeBackend();
                services.AddSingleton(fake);
                services.AddSingleton<IBackendFactory>(new FakeBackendFactory(fake));
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
}
