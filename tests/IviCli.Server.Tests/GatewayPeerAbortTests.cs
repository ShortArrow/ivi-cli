using System.Net;
using System.Net.Sockets;
using System.Text;
using IviCli.Backends.Fake;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Protocols;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.Server.HiSlip;
using IviCli.Server.UsbIp;
using IviCli.Server.Vxi11;
using IviCli.TestKit;
using Microsoft.Extensions.Logging;
using Shouldly;
using static IviCli.Domain.Protocols.Vxi11Constants;

namespace IviCli.Server.Tests;

/// <summary>
/// A client that resets its connection after the protocol handshake ends the
/// session like any other disconnect on every gateway: one line, never an
/// unexpected error. The SOCKET case is in <see cref="SocketPeerAbortTests"/>.
/// </summary>
public sealed class GatewayPeerAbortTests
{
    [Fact]
    public async Task HiSlip_client_reset_after_initialize_is_not_an_error()
    {
        var (server, config) = Configure(ServerType.HiSlip, "hislip0", "hislip0::INSTR");
        var logger = new RecordingLogger<HiSlipGatewayServer>();
        var gateway = new HiSlipGatewayServer(new FakeBackendFactory(new FakeBackend()), logger);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(server.Port.Value, cts.Token);

        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, server.Port.Value, cts.Token);
            var stream = tcp.GetStream();
            var subAddress = "hislip0"u8.ToArray();
            var header = new byte[HiSlipMessage.HeaderSize];
            HiSlipMessage.WriteHeader(
                header,
                HiSlipMessageType.Initialize,
                controlCode: 0,
                messageParameter: 0x0100_0000,
                payloadLength: (ulong)subAddress.Length
            );
            await stream.WriteAsync(header, cts.Token);
            await stream.WriteAsync(subAddress, cts.Token);
            await stream.ReadExactlyAsync(new byte[HiSlipMessage.HeaderSize], cts.Token);
            tcp.Client.Close(0);
        }

        var ending = await EndingAsync(logger, cts.Token);
        await StopAsync(cts, serverTask);
        ending.Level.ShouldBe(LogLevel.Information);
        ending.Message.ShouldContain("client aborted the connection");
    }

    [Fact]
    public async Task Vxi11_client_reset_after_create_link_is_not_an_error()
    {
        var (server, config) = Configure(ServerType.Vxi11, "inst0", "inst0::INSTR");
        var logger = new RecordingLogger<Vxi11GatewayServer>();
        var gateway = new Vxi11GatewayServer(new FakeBackendFactory(new FakeBackend()), logger)
        {
            PortmapUdpPort = GetFreePort(),
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(server.Port.Value, cts.Token);

        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, server.Port.Value, cts.Token);
            var stream = tcp.GetStream();
            var writer = new Vxi11XdrCodec.XdrWriter();
            writer.WriteUInt32(1); // xid
            writer.WriteUInt32(0); // CALL
            writer.WriteUInt32(2); // rpcvers
            writer.WriteUInt32(CoreProgram);
            writer.WriteUInt32(CoreVersion);
            writer.WriteUInt32(ProcCreateLink);
            writer.WriteUInt32(0);
            writer.WriteOpaque([]);
            writer.WriteUInt32(0);
            writer.WriteOpaque([]);
            writer.WriteInt32(1); // clientId
            writer.WriteUInt32(0); // lockDevice
            writer.WriteUInt32(0); // lock_timeout
            writer.WriteString("inst0");
            await Vxi11RecordFraming.WriteRecordAsync(stream, writer.ToArray(), cts.Token);
            _ = await Vxi11RecordFraming.ReadRecordAsync(stream, cts.Token);
            tcp.Client.Close(0);
        }

        var ending = await EndingAsync(logger, cts.Token);
        await StopAsync(cts, serverTask);
        ending.Level.ShouldBe(LogLevel.Information);
        ending.Message.ShouldContain("client aborted the connection");
    }

    [Fact]
    public async Task UsbIp_client_reset_after_import_is_not_an_error()
    {
        var logger = new RecordingLogger<UsbIpGatewayServer>();
        await using var bench = await UsbIpBench.StartAsync(
            logger,
            (UsbIpBench.BusId, UsbExportProfile.UsbTmc, UsbIpBench.DefaultDeviceName)
        );
        using (var client = await bench.ImportAsync())
        {
            client.Abort();
        }

        var ending = await EndingAsync(logger, bench.Token);
        ending.Level.ShouldBe(LogLevel.Debug);
        ending.Message.ShouldContain("I/O error");
    }

    /// <summary>
    /// The first entry the gateway logs about how the connection ended: an
    /// error, or the line that names the abort.
    /// </summary>
    private static async Task<(LogLevel Level, string Message)> EndingAsync<T>(
        RecordingLogger<T> logger,
        CancellationToken ct
    )
    {
        while (true)
        {
            var ending = logger.Entries.FirstOrDefault(e =>
                e.Level >= LogLevel.Error
                || e.Message.Contains("aborted")
                || e.Message.Contains("I/O error")
            );
            if (ending.Message is not null)
            {
                return ending;
            }
            await Task.Delay(20, ct);
        }
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

    private static (IviCli.Domain.Servers.Server Server, ConfigDocument Config) Configure(
        ServerType type,
        string endpoint,
        string resourceSuffix
    )
    {
        var deviceName = DeviceName.From("dut").ShouldBeOk();
        var device = new Device(
            deviceName,
            VisaResource.Parse($"TCPIP0::127.0.0.1::{resourceSuffix}").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );
        var serverName = ServerName.From("srv").ShouldBeOk();
        var server = new IviCli.Domain.Servers.Server(
            serverName,
            type,
            IpAddress.From("127.0.0.1").ShouldBeOk(),
            Port.From(GetFreePort()).ShouldBeOk()
        );
        var config = ConfigDocument
            .Empty.AddDevice(device)
            .ShouldBeOk()
            .AddServer(server)
            .ShouldBeOk()
            .AddRoute(new Route(serverName, PublicEndpoint.From(endpoint).ShouldBeOk(), deviceName))
            .ShouldBeOk();
        return (server, config);
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
