using System.Net;
using System.Net.Sockets;
using System.Text;
using IviCli.Backends.Fake;
using IviCli.Backends.HiSlip;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Protocols;
using IviCli.Domain.Scpi;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.Server.HiSlip;
using IviCli.Server.Vxi11;
using IviCli.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using static IviCli.Domain.Protocols.Vxi11Constants;

namespace IviCli.Server.Tests;

/// <summary>
/// A message that is only whitespace and a terminator is an empty program
/// message (IEEE 488.2 §7): an instrument accepts it and does nothing. Every
/// gateway does the same, and the session carries on.
/// </summary>
public sealed class GatewayEmptyMessageTests
{
    [Theory]
    [InlineData("  ")]
    [InlineData("\t")]
    public async Task HiSlip_keeps_the_session_after_an_empty_message(string text)
    {
        var (server, config, device) = Configure(ServerType.HiSlip, "hislip0", "hislip0::INSTR");
        var fake = new FakeBackend().RespondToQuery(device.Name, "*IDN?", "FAKE,HISLIP,0,1.0");
        var gateway = new HiSlipGatewayServer(
            new FakeBackendFactory(fake),
            NullLogger<HiSlipGatewayServer>.Instance
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(server.Port.Value, cts.Token);

        var client = new HiSlipBackend(server.Port.Value);
        (await client.OpenAsync(device, cts.Token)).ShouldBeOk();
        (
            await client.WriteAsync(device, ScpiCommand.From(text).ShouldBeOk(), cts.Token)
        ).ShouldBeOk();
        (await client.QueryAsync(device, ScpiQuery.From("*IDN?").ShouldBeOk(), cts.Token))
            .ShouldBeOk()
            .ShouldBe("FAKE,HISLIP,0,1.0");

        await client.CloseAsync(device, cts.Token);
        await StopAsync(cts, serverTask);
    }

    [Theory]
    [InlineData("  \n")]
    [InlineData("\n")]
    public async Task Vxi11_accepts_an_empty_message_as_a_write(string text)
    {
        var (server, config, _) = Configure(ServerType.Vxi11, "inst0", "inst0::INSTR");
        var gateway = new Vxi11GatewayServer(
            new FakeBackendFactory(new FakeBackend()),
            NullLogger<Vxi11GatewayServer>.Instance
        )
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
            var create = await CallAsync(
                stream,
                ProcCreateLink,
                w =>
                {
                    w.WriteInt32(1);
                    w.WriteUInt32(0);
                    w.WriteUInt32(0);
                    w.WriteString("inst0");
                },
                cts.Token
            );
            create.ReadInt32().ShouldBe(Vxi11NoError);
            var lid = create.ReadInt32();

            var data = Encoding.ASCII.GetBytes(text);
            var write = await CallAsync(
                stream,
                ProcDeviceWrite,
                w =>
                {
                    w.WriteInt32(lid);
                    w.WriteUInt32(1000);
                    w.WriteUInt32(0);
                    w.WriteInt32(WriteEndFlag);
                    w.WriteOpaque(data);
                },
                cts.Token
            );
            write.ReadInt32().ShouldBe(Vxi11NoError);
            write.ReadUInt32().ShouldBe((uint)data.Length);
        }

        await StopAsync(cts, serverTask);
    }

    private static async Task<Vxi11XdrCodec.XdrReader> CallAsync(
        NetworkStream stream,
        uint procedure,
        Action<Vxi11XdrCodec.XdrWriter> body,
        CancellationToken ct
    )
    {
        var writer = new Vxi11XdrCodec.XdrWriter();
        writer.WriteUInt32(procedure); // xid
        writer.WriteUInt32(0); // CALL
        writer.WriteUInt32(2); // rpcvers
        writer.WriteUInt32(CoreProgram);
        writer.WriteUInt32(CoreVersion);
        writer.WriteUInt32(procedure);
        writer.WriteUInt32(0);
        writer.WriteOpaque([]);
        writer.WriteUInt32(0);
        writer.WriteOpaque([]);
        body(writer);
        await Vxi11RecordFraming.WriteRecordAsync(stream, writer.ToArray(), ct);
        var reader = new Vxi11XdrCodec.XdrReader(
            await Vxi11RecordFraming.ReadRecordAsync(stream, ct)
        );
        _ = reader.ReadUInt32(); // xid
        _ = reader.ReadUInt32(); // mtype
        _ = reader.ReadUInt32(); // reply_stat
        _ = reader.ReadUInt32(); // verf flavor
        _ = reader.ReadOpaque(); // verf body
        _ = reader.ReadUInt32(); // accept_stat
        return reader;
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

    private static (
        IviCli.Domain.Servers.Server Server,
        ConfigDocument Config,
        Device Device
    ) Configure(ServerType type, string endpoint, string resourceSuffix)
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
        return (server, config, device);
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
