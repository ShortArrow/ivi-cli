using System.Net;
using System.Net.Sockets;
using System.Text;
using IviCli.Backends.Fake;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.Server.Socket;
using IviCli.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace IviCli.Server.Tests;

public sealed class LoopbackGatewayTests
{
    [Fact]
    public async Task A_gateway_whose_port_is_taken_is_started_on_another_port()
    {
        using var squatter = new TcpListener(IPAddress.Loopback, 0);
        squatter.Start();
        var takenPort = ((IPEndPoint)squatter.LocalEndpoint).Port;
        var (server, config) = BuildSocketTopology(takenPort);
        var fake = new FakeBackend().RespondToQuery(
            DeviceName.From("dut").ShouldBeOk(),
            "*IDN?",
            "FAKE,LOOPBACK,0,1.0"
        );
        var gateway = new SocketGatewayServer(
            new FakeBackendFactory(fake),
            NullLogger<SocketGatewayServer>.Instance
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var (port, serverTask) = LoopbackGateway.Start(
            server,
            s => gateway.RunAsync(s, config, cts.Token)
        );

        port.ShouldNotBe(takenPort);
        serverTask.IsCompleted.ShouldBeFalse();
        (await QueryAsync(port, "*IDN?", cts.Token)).ShouldBe("FAKE,LOOPBACK,0,1.0");

        await cts.CancelAsync();
        (await serverTask).ShouldBeOk();
    }

    private static (IviCli.Domain.Servers.Server Server, ConfigDocument Config) BuildSocketTopology(
        int port
    )
    {
        var deviceName = DeviceName.From("dut").ShouldBeOk();
        var device = new Device(
            deviceName,
            VisaResource.Parse("TCPIP0::127.0.0.1::5025::SOCKET").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );
        var server = new IviCli.Domain.Servers.Server(
            ServerName.From("loopback-srv").ShouldBeOk(),
            ServerType.Socket,
            IpAddress.From("127.0.0.1").ShouldBeOk(),
            Port.From(port).ShouldBeOk()
        );
        var config = ConfigDocument
            .Empty.AddDevice(device)
            .ShouldBeOk()
            .AddServer(server)
            .ShouldBeOk()
            .AddRoute(
                new Route(server.Name, PublicEndpoint.From("socket0").ShouldBeOk(), deviceName)
            )
            .ShouldBeOk();
        return (server, config);
    }

    private static async Task<string> QueryAsync(int port, string query, CancellationToken ct)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, ct);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(query + "\n"), ct);
        using var reader = new StreamReader(stream, Encoding.ASCII);
        return (await reader.ReadLineAsync(ct))!;
    }
}
