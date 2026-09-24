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

namespace IviCli.Server.Tests;

/// <summary>
/// The SOCKET gateway answers a query whose header ends in <c>?</c> even
/// when parameters follow it, and still answers every line that ends in
/// <c>?</c>, as 0.3.1 did.
/// </summary>
public sealed class SocketQueryDetectionTests
{
    [Theory]
    [InlineData("MEAS:VOLT? CH1", "MEAS:VOLT? CH1")]
    [InlineData("MEAS:VOLT? (@1)", "MEAS:VOLT? (@1)")]
    [InlineData("VOLT MAX?", "VOLT MAX?")]
    [InlineData("*IDN?", "FAKE,SOCKET,0,1.0")]
    public async Task A_query_gets_its_reply(string request, string expected)
    {
        var port = GetFreePort();
        var deviceName = DeviceName.From("dut").ShouldBeOk();
        var device = new Device(
            deviceName,
            VisaResource.Parse("TCPIP0::127.0.0.1::5025::SOCKET").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );
        var serverName = ServerName.From("socket-srv").ShouldBeOk();
        var config = ConfigDocument
            .Empty.AddDevice(device)
            .ShouldBeOk()
            .AddServer(
                new IviCli.Domain.Servers.Server(
                    serverName,
                    ServerType.Socket,
                    IpAddress.From("127.0.0.1").ShouldBeOk(),
                    Port.From(port).ShouldBeOk()
                )
            )
            .ShouldBeOk()
            .AddRoute(
                new Route(serverName, PublicEndpoint.From("socket0").ShouldBeOk(), deviceName)
            )
            .ShouldBeOk();
        var fake = new FakeBackend().ConfigureDevice(deviceName, "FAKE,SOCKET,0,1.0");
        var gateway = new SocketGatewayServer(
            new FakeBackendFactory(fake),
            NullLogger<SocketGatewayServer>.Instance
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(config.Servers.Single(), config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);

        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, port, cts.Token);
            var stream = tcp.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(request + "\n"), cts.Token);
            using var replyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            replyTimeout.CancelAfter(TimeSpan.FromSeconds(2));
            (await reader.ReadLineAsync(replyTimeout.Token)).ShouldBe(expected);
        }

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
        for (var attempt = 0; attempt < 100; attempt++)
        {
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
        throw new TimeoutException($"gateway did not start listening on {port}");
    }
}
