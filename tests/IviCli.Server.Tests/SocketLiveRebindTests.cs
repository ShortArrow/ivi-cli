using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Text;
using IviCli.Backends.Fake;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;
using IviCli.Domain.Servers;
using IviCli.Domain.Session;
using IviCli.Domain.Visa;
using IviCli.Server.Socket;
using IviCli.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace IviCli.Server.Tests;

/// <summary>
/// A serving SOCKET gateway must reflect a separate-process
/// <c>mock scenario activate</c> on the next request of an already-open
/// connection — without the client reconnecting or the gateway restarting.
/// </summary>
public sealed class SocketLiveRebindTests
{
    [Fact]
    public async Task Live_rebind_is_observed_mid_connection()
    {
        var deviceName = DeviceName.From("dut").ShouldBeOk();
        var device = new Device(
            deviceName,
            VisaResource.Parse("TCPIP0::127.0.0.1::5025::SOCKET").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

        // Backend seeded with scenario "a" at startup (mirrors Program.cs).
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

        var (server, config, port) = BuildHarness(device);
        var gateway = new SocketGatewayServer(
            new FakeBackendFactory(fake),
            NullLogger<SocketGatewayServer>.Instance,
            refresher
        );

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        using var stream = tcp.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true)
        {
            NewLine = "\n",
            AutoFlush = true,
        };

        // Before: scenario "a".
        await writer.WriteLineAsync("MEAS:VOLT?");
        (await reader.ReadLineAsync(cts.Token)).ShouldBe("1.00");

        // A separate process activates "b" (writes only the session store).
        await sessions.SaveAsync(SessionWith(deviceName, "b"), cts.Token);

        // After: the same connection now serves scenario "b".
        await writer.WriteLineAsync("MEAS:VOLT?");
        (await reader.ReadLineAsync(cts.Token)).ShouldBe("2.00");

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

    private static (
        IviCli.Domain.Servers.Server Server,
        ConfigDocument Config,
        int Port
    ) BuildHarness(Device device)
    {
        var port = GetFreePort();
        var serverName = ServerName.From("socket-srv").ShouldBeOk();
        var endpoint = PublicEndpoint.From("socket0").ShouldBeOk();
        var bind = IpAddress.From("127.0.0.1").ShouldBeOk();
        var portValue = Port.From(port).ShouldBeOk();
        var server = new IviCli.Domain.Servers.Server(
            serverName,
            ServerType.Socket,
            bind,
            portValue
        );
        var route = new Route(serverName, endpoint, device.Name);
        var config = ConfigDocument
            .Empty.AddDevice(device)
            .ShouldBeOk()
            .AddServer(server)
            .ShouldBeOk()
            .AddRoute(route)
            .ShouldBeOk();
        return (server, config, port);
    }

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
        throw new TimeoutException($"SOCKET gateway did not bind to port {port}");
    }
}
