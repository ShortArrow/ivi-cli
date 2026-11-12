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
using Microsoft.Extensions.Logging;
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
        var port = LoopbackGateway.FreePort();
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
        (port, var serverTask) = LoopbackGateway.Start(
            server,
            s => gateway.RunAsync(s, config, cts.Token)
        );
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

    /// <summary>
    /// A device whose backend exists only while a binding names a scenario,
    /// bound after the gateway started, answers the next link from it.
    /// </summary>
    [Fact]
    public async Task A_binding_made_while_serving_answers_the_next_link()
    {
        var port = LoopbackGateway.FreePort();
        var deviceName = DeviceName.From("dut").ShouldBeOk();
        var device = new Device(
            deviceName,
            VisaResource.Parse("TCPIP0::127.0.0.1::hislip0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );
        var serverName = ServerName.From("hislip-srv").ShouldBeOk();
        var server = new IviCli.Domain.Servers.Server(
            serverName,
            ServerType.HiSlip,
            IpAddress.From("127.0.0.1").ShouldBeOk(),
            Port.From(port).ShouldBeOk()
        );
        var config = ConfigDocument
            .Empty.AddDevice(device)
            .ShouldBeOk()
            .AddServer(server)
            .ShouldBeOk()
            .AddRoute(
                new Route(serverName, PublicEndpoint.From("hislip0").ShouldBeOk(), deviceName)
            )
            .ShouldBeOk();

        var scenarioA = VoltScenario("a", "1.00");
        var fake = new FakeBackend();
        var sessions = new FakeSessionStore(SessionState.Empty);
        var refresher = new SessionScenarioBindingRefresher(
            fake,
            new FakeScenarioStore(new[] { scenarioA }),
            sessions,
            NullLogger<SessionScenarioBindingRefresher>.Instance
        );

        var gateway = new HiSlipGatewayServer(
            new BindingGatedBackendFactory(fake),
            NullLogger<HiSlipGatewayServer>.Instance,
            refresher
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        (port, var serverTask) = LoopbackGateway.Start(
            server,
            s => gateway.RunAsync(s, config, cts.Token)
        );
        await WaitForListenerAsync(port, cts.Token);

        await sessions.SaveAsync(SessionWith(deviceName, "a"), cts.Token);

        var client = new HiSlipBackend(port);
        (await client.OpenAsync(device, cts.Token)).ShouldBeOk();
        (await client.QueryAsync(device, ScpiQuery.From("MEAS:VOLT?").ShouldBeOk(), cts.Token))
            .ShouldBeOk()
            .ShouldBe("1.00");

        await client.CloseAsync(device, cts.Token);
        await cts.CancelAsync();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// A device with no backend until a binding names a scenario fails the
    /// first request with a FatalError, since the gateway answers Initialize
    /// before it chooses the backend, and the log says why.
    /// </summary>
    [Fact]
    public async Task An_unbound_device_fails_the_first_request_and_the_log_says_why()
    {
        var port = LoopbackGateway.FreePort();
        var deviceName = DeviceName.From("dut").ShouldBeOk();
        var device = new Device(
            deviceName,
            VisaResource.Parse("TCPIP0::127.0.0.1::hislip0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );
        var serverName = ServerName.From("hislip-srv").ShouldBeOk();
        var server = new IviCli.Domain.Servers.Server(
            serverName,
            ServerType.HiSlip,
            IpAddress.From("127.0.0.1").ShouldBeOk(),
            Port.From(port).ShouldBeOk()
        );
        var config = ConfigDocument
            .Empty.AddDevice(device)
            .ShouldBeOk()
            .AddServer(server)
            .ShouldBeOk()
            .AddRoute(
                new Route(serverName, PublicEndpoint.From("hislip0").ShouldBeOk(), deviceName)
            )
            .ShouldBeOk();
        var logger = new RecordingLogger<HiSlipGatewayServer>();
        var gateway = new HiSlipGatewayServer(
            new BindingGatedBackendFactory(new FakeBackend()),
            logger
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        (port, var serverTask) = LoopbackGateway.Start(
            server,
            s => gateway.RunAsync(s, config, cts.Token)
        );
        await WaitForListenerAsync(port, cts.Token);

        var client = new HiSlipBackend(port);
        (await client.OpenAsync(device, cts.Token)).ShouldBeOk();
        (
            await client.QueryAsync(device, ScpiQuery.From("MEAS:VOLT?").ShouldBeOk(), cts.Token)
        ).ShouldBeError();

        logger.Entries.ShouldContain(e =>
            e.Level == LogLevel.Error && e.Message.Contains("no scenario is bound")
        );

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
