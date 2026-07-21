using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using IviCli.Backends.Fake;
using IviCli.Backends.HiSlip;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;
using IviCli.Domain.Scpi;
using IviCli.Domain.Servers;
using IviCli.Domain.Session;
using IviCli.Domain.Visa;
using IviCli.Server.HiSlip;
using IviCli.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace IviCli.Server.Tests;

/// <summary>
/// A serving HiSLIP gateway must reflect a separate-process
/// <c>mock scenario activate</c> on the next query of an already-open link —
/// without the client reconnecting or the gateway restarting.
/// </summary>
public sealed class HiSlipLiveRebindTests
{
    [Fact]
    public async Task Live_rebind_is_observed_mid_link()
    {
        var port = GetFreePort();
        var deviceName = DeviceName.From("dut").ShouldBeOk();
        var device = new Device(
            deviceName,
            VisaResource.Parse("TCPIP0::127.0.0.1::hislip0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );
        var serverName = ServerName.From("hislip-srv").ShouldBeOk();
        var endpoint = PublicEndpoint.From("hislip0").ShouldBeOk();
        var bind = IpAddress.From("127.0.0.1").ShouldBeOk();
        var portValue = Port.From(port).ShouldBeOk();
        var server = new IviCli.Domain.Servers.Server(
            serverName,
            ServerType.HiSlip,
            bind,
            portValue
        );
        var route = new Route(serverName, endpoint, deviceName);
        var config = ConfigDocument
            .Empty.AddDevice(device)
            .ShouldBeOk()
            .AddServer(server)
            .ShouldBeOk()
            .AddRoute(route)
            .ShouldBeOk();

        var scenarioA = VoltScenario("a", "1.00");
        var scenarioB = VoltScenario("b", "2.00");
        var fake = new FakeBackend();
        fake.ActivateScenario(scenarioA, deviceName);

        var sessions = new FakeSessionStore(SessionWith(deviceName, "a"));
        var refresher = new SessionScenarioBindingRefresher(
            fake,
            new FakeScenarioStore(new[] { scenarioA, scenarioB }),
            sessions,
            NullLogger<SessionScenarioBindingRefresher>.Instance
        );

        var gateway = new HiSlipGatewayServer(
            new FakeBackendFactory(fake),
            NullLogger<HiSlipGatewayServer>.Instance,
            refresher
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);

        var client = new HiSlipBackend(port);
        (await client.OpenAsync(device, cts.Token)).ShouldBeOk();

        var query = ScpiQuery.From("MEAS:VOLT?").ShouldBeOk();

        // Before: scenario "a".
        (await client.QueryAsync(device, query, cts.Token))
            .ShouldBeOk()
            .ShouldBe("1.00");

        // A separate process activates "b" (writes only the session store).
        await sessions.SaveAsync(SessionWith(deviceName, "b"), cts.Token);

        // After: the same link now serves scenario "b".
        (await client.QueryAsync(device, query, cts.Token))
            .ShouldBeOk()
            .ShouldBe("2.00");

        await client.CloseAsync(device, cts.Token);
        await cts.CancelAsync();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException) { }
    }

    private static MockScenario VoltScenario(string name, string volts) =>
        MockScenario.SingleScene(
            ScenarioName.From(name).ShouldBeOk(),
            idnDefault: null,
            rules: ImmutableArray.Create(new MockRule("MEAS:VOLT?", new RuleAction.Respond(volts)))
        );

    private static SessionState SessionWith(DeviceName device, string scenario) =>
        SessionState.Empty.BindScenario(device, ScenarioName.From(scenario).ShouldBeOk());

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task WaitForListenerAsync(int port, CancellationToken ct)
    {
        for (var i = 0; i < 50; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync(IPAddress.Loopback, port, ct);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(50, ct);
            }
        }
        throw new TimeoutException($"HiSLIP gateway did not bind to port {port}");
    }
}
