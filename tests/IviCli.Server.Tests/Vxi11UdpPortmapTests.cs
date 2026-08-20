using System.Net;
using System.Net.Sockets;
using IviCli.Backends.Fake;
using IviCli.Backends.Vxi11;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.Server.Vxi11;
using IviCli.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace IviCli.Server.Tests;

/// <summary>
/// The gateway's best-effort UDP portmapper (issue #14): a GETPORT
/// datagram — the transport `visa scan`'s broadcast probe and unicast
/// clients actually use — is answered with the gateway's TCP port, and
/// UDP noise neither crashes the responder nor earns a reply.
/// </summary>
public sealed class Vxi11UdpPortmapTests
{
    [Fact]
    public async Task A_getport_datagram_is_answered_with_the_core_tcp_port()
    {
        var (gateway, server, config, tcpPort, udpPort) = BuildHarness();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(tcpPort, cts.Token);

        var resolved = await Vxi11Portmapper.ResolveCorePortAsync(
            "127.0.0.1",
            udpPort,
            TimeSpan.FromSeconds(5),
            cts.Token
        );

        resolved.ShouldBe(tcpPort);
        await StopAsync(cts, serverTask);
    }

    [Fact]
    public async Task Udp_noise_is_ignored_and_the_responder_keeps_answering()
    {
        var (gateway, server, config, tcpPort, udpPort) = BuildHarness();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(tcpPort, cts.Token);

        using (var noise = new UdpClient(AddressFamily.InterNetwork))
        {
            noise.Connect("127.0.0.1", udpPort);
            await noise.SendAsync(new byte[] { 0x00, 0x01, 0x02 }, cts.Token);
        }

        var resolved = await Vxi11Portmapper.ResolveCorePortAsync(
            "127.0.0.1",
            udpPort,
            TimeSpan.FromSeconds(5),
            cts.Token
        );

        resolved.ShouldBe(tcpPort);
        await StopAsync(cts, serverTask);
    }

    private static (
        Vxi11GatewayServer Gateway,
        IviCli.Domain.Servers.Server Server,
        ConfigDocument Config,
        int TcpPort,
        int UdpPort
    ) BuildHarness()
    {
        var tcpPort = GetFreeTcpPort();
        var udpPort = GetFreeUdpPort();
        var deviceName = DeviceName.From("dut").ShouldBeOk();
        var device = new Device(
            deviceName,
            VisaResource.Parse("TCPIP0::127.0.0.1::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );
        var serverName = ServerName.From("vxi11-udp-srv").ShouldBeOk();
        var server = new IviCli.Domain.Servers.Server(
            serverName,
            ServerType.Vxi11,
            IpAddress.From("127.0.0.1").ShouldBeOk(),
            Port.From(tcpPort).ShouldBeOk()
        );
        var route = new Route(serverName, PublicEndpoint.From("inst0").ShouldBeOk(), deviceName);
        var config = ConfigDocument
            .Empty.AddDevice(device)
            .ShouldBeOk()
            .AddServer(server)
            .ShouldBeOk()
            .AddRoute(route)
            .ShouldBeOk();
        var fake = new FakeBackend().ConfigureDevice(deviceName, "FAKE,VXI11,0,1.0");
        var gateway = new Vxi11GatewayServer(
            new FakeBackendFactory(fake),
            NullLogger<Vxi11GatewayServer>.Instance
        )
        {
            PortmapUdpPort = udpPort,
        };
        return (gateway, server, config, tcpPort, udpPort);
    }

    private static async Task StopAsync(CancellationTokenSource cts, Task serverTask)
    {
        await cts.CancelAsync();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException) { }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static int GetFreeUdpPort()
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
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
        throw new TimeoutException($"gateway did not bind to port {port}");
    }
}
