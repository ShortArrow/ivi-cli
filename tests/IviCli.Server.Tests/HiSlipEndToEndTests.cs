using System.Net;
using System.Net.Sockets;
using IviCli.Application.Backends;
using IviCli.Backends.Fake;
using IviCli.Backends.HiSlip;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.Server.HiSlip;
using IviCli.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace IviCli.Server.Tests;

/// <summary>
/// End-to-end pairing of <see cref="HiSlipGatewayServer"/> (server) and
/// <see cref="HiSlipBackend"/> (client) routed against the in-memory
/// <see cref="FakeBackend"/>. Verifies handshake + Data/DataEnd round-trip
/// across a real TcpListener on the loopback interface.
/// </summary>
public sealed class HiSlipEndToEndTests
{
    [Fact]
    public async Task Query_returns_fake_response_through_gateway()
    {
        var port = GetFreePort();
        var deviceName = DeviceName.From("dut").ShouldBeOk();
        var device = new Device(
            deviceName,
            VisaResource.Parse("TCPIP0::127.0.0.1::hislip0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );
        var serverName = ServerName.From("hislip-srv").ShouldBeOk();
        // Match the client's LanDevice ("hislip0") so the gateway's
        // sub-address router (issue #21) resolves this single route.
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

        var fake = new FakeBackend()
            .ConfigureDevice(deviceName, "FAKE,HISLIP,0,1.0")
            .RespondToQuery(deviceName, "*IDN?", "FAKE,HISLIP,0,1.0");

        var gateway = new HiSlipGatewayServer(
            new FakeBackendFactory(fake),
            NullLogger<HiSlipGatewayServer>.Instance
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);

        await WaitForListenerAsync(port, cts.Token);

        var client = new HiSlipBackend(port);
        (await client.OpenAsync(device, cts.Token)).ShouldBeOk();

        var query = ScpiQuery.From("*IDN?").ShouldBeOk();
        var response = await client.QueryAsync(device, query, cts.Token);
        response.ShouldBeOk().ShouldBe("FAKE,HISLIP,0,1.0");

        await client.CloseAsync(device, cts.Token);
        await cts.CancelAsync();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task Query_uses_explicit_resource_port_over_backend_default()
    {
        var port = GetFreePort();
        var deviceName = DeviceName.From("dut").ShouldBeOk();
        // Resource carries the gateway's port via the `hislip0,<port>` form.
        var device = new Device(
            deviceName,
            VisaResource.Parse($"TCPIP0::127.0.0.1::hislip0,{port}::INSTR").ShouldBeOk(),
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

        var fake = new FakeBackend()
            .ConfigureDevice(deviceName, "FAKE,HISLIP,0,1.0")
            .RespondToQuery(deviceName, "*IDN?", "FAKE,HISLIP,0,1.0");

        var gateway = new HiSlipGatewayServer(
            new FakeBackendFactory(fake),
            NullLogger<HiSlipGatewayServer>.Instance
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);

        await WaitForListenerAsync(port, cts.Token);

        // Default constructor → well-known 4880, which is NOT where the gateway
        // listens. Success proves the resource port (hislip0,<port>) won.
        var client = new HiSlipBackend();
        (await client.OpenAsync(device, cts.Token)).ShouldBeOk();

        var query = ScpiQuery.From("*IDN?").ShouldBeOk();
        (await client.QueryAsync(device, query, cts.Token))
            .ShouldBeOk()
            .ShouldBe("FAKE,HISLIP,0,1.0");

        await client.CloseAsync(device, cts.Token);
        await cts.CancelAsync();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException) { }
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
        throw new TimeoutException($"HiSLIP gateway did not bind to port {port}");
    }
}
