using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using IviCli.Backends.Fake;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Protocols;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.Server.Vxi11;
using IviCli.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using static IviCli.Domain.Protocols.Vxi11Constants;

namespace IviCli.Server.Tests;

/// <summary>
/// Raw ONC RPC / XDR round-trip against an in-proc
/// <see cref="Vxi11GatewayServer"/>. The tests speak the wire format
/// directly (no third-party client) so a regression in framing or
/// procedure encoding surfaces here without dragging in PyVISA.
/// </summary>
public sealed class Vxi11EndToEndTests
{
    [Fact]
    public async Task Portmap_GetPort_returns_bound_port_for_core_program()
    {
        var (gateway, server, config, port, fake) = BuildHarness();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);
        _ = fake;

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        using var stream = tcp.GetStream();

        var call = BuildRpcCall(
            xid: 1,
            program: PortmapProgram,
            version: PortmapVersion,
            procedure: PortmapGetPort,
            body: writer =>
            {
                writer.WriteUInt32(CoreProgram);
                writer.WriteUInt32(CoreVersion);
                writer.WriteUInt32(6); // TCP
                writer.WriteUInt32(0);
            }
        );
        await Vxi11RecordFraming.WriteRecordAsync(stream, call, cts.Token);
        var reply = await Vxi11RecordFraming.ReadRecordAsync(stream, cts.Token);
        var reader = SkipReplyHeader(reply);
        reader.ReadUInt32().ShouldBe((uint)port);

        await cts.CancelAsync();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task CreateLink_then_Write_query_returns_fake_response()
    {
        var (gateway, server, config, port, fake) = BuildHarness();
        fake.RespondToQuery(DeviceName.From("dut").ShouldBeOk(), "*IDN?", "FAKE,VXI11,0,1.0");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        using var stream = tcp.GetStream();

        // create_link
        var createCall = BuildRpcCall(
            xid: 100,
            program: CoreProgram,
            version: CoreVersion,
            procedure: ProcCreateLink,
            body: writer =>
            {
                writer.WriteInt32(1); // clientId
                writer.WriteUInt32(0); // lockDevice=false
                writer.WriteUInt32(0); // lock_timeout
                writer.WriteString("inst0");
            }
        );
        await Vxi11RecordFraming.WriteRecordAsync(stream, createCall, cts.Token);
        var createReply = SkipReplyHeader(
            await Vxi11RecordFraming.ReadRecordAsync(stream, cts.Token)
        );
        createReply.ReadInt32().ShouldBe(Vxi11NoError);
        var lid = createReply.ReadInt32();
        _ = createReply.ReadUInt32(); // abort port
        _ = createReply.ReadUInt32(); // maxRecvSize

        // device_write *IDN?\n with END flag
        var idn = "*IDN?\n"u8.ToArray();
        var writeCall = BuildRpcCall(
            xid: 101,
            program: CoreProgram,
            version: CoreVersion,
            procedure: ProcDeviceWrite,
            body: writer =>
            {
                writer.WriteInt32(lid);
                writer.WriteUInt32(1000); // io timeout
                writer.WriteUInt32(0); // lock timeout
                writer.WriteInt32(WriteEndFlag);
                writer.WriteOpaque(idn);
            }
        );
        await Vxi11RecordFraming.WriteRecordAsync(stream, writeCall, cts.Token);
        var writeReply = SkipReplyHeader(
            await Vxi11RecordFraming.ReadRecordAsync(stream, cts.Token)
        );
        writeReply.ReadInt32().ShouldBe(Vxi11NoError);
        writeReply.ReadUInt32().ShouldBe((uint)idn.Length);

        // device_read
        var readCall = BuildRpcCall(
            xid: 102,
            program: CoreProgram,
            version: CoreVersion,
            procedure: ProcDeviceRead,
            body: writer =>
            {
                writer.WriteInt32(lid);
                writer.WriteUInt32(4096); // requestSize
                writer.WriteUInt32(1000); // io timeout
                writer.WriteUInt32(0); // lock timeout
                writer.WriteInt32(0);
                writer.WriteUInt32((byte)'\n');
            }
        );
        await Vxi11RecordFraming.WriteRecordAsync(stream, readCall, cts.Token);
        var readReply = SkipReplyHeader(
            await Vxi11RecordFraming.ReadRecordAsync(stream, cts.Token)
        );
        readReply.ReadInt32().ShouldBe(Vxi11NoError);
        readReply.ReadInt32().ShouldBe(4); // END reason
        var data = readReply.ReadOpaque();
        System.Text.Encoding.ASCII.GetString(data).ShouldBe("FAKE,VXI11,0,1.0");

        // destroy_link
        var destroyCall = BuildRpcCall(
            xid: 103,
            program: CoreProgram,
            version: CoreVersion,
            procedure: ProcDestroyLink,
            body: writer => writer.WriteInt32(lid)
        );
        await Vxi11RecordFraming.WriteRecordAsync(stream, destroyCall, cts.Token);
        var destroyReply = SkipReplyHeader(
            await Vxi11RecordFraming.ReadRecordAsync(stream, cts.Token)
        );
        destroyReply.ReadInt32().ShouldBe(Vxi11NoError);

        await cts.CancelAsync();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task DeviceWrite_with_invalid_link_returns_invalid_link_error()
    {
        var (gateway, server, config, port, _) = BuildHarness();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        using var stream = tcp.GetStream();

        var bogusCall = BuildRpcCall(
            xid: 200,
            program: CoreProgram,
            version: CoreVersion,
            procedure: ProcDeviceWrite,
            body: writer =>
            {
                writer.WriteInt32(9999); // invalid link
                writer.WriteUInt32(1000);
                writer.WriteUInt32(0);
                writer.WriteInt32(WriteEndFlag);
                writer.WriteOpaque("*IDN?\n"u8.ToArray());
            }
        );
        await Vxi11RecordFraming.WriteRecordAsync(stream, bogusCall, cts.Token);
        var reply = SkipReplyHeader(await Vxi11RecordFraming.ReadRecordAsync(stream, cts.Token));
        reply.ReadInt32().ShouldBe(Vxi11InvalidLink);

        await cts.CancelAsync();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task Portmap_GetPort_returns_bound_port_for_abort_program()
    {
        var (gateway, server, config, port, _) = BuildHarness();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        using var stream = tcp.GetStream();

        var call = BuildRpcCall(
            xid: 11,
            program: PortmapProgram,
            version: PortmapVersion,
            procedure: PortmapGetPort,
            body: writer =>
            {
                writer.WriteUInt32(AbortProgram);
                writer.WriteUInt32(AbortVersion);
                writer.WriteUInt32(6); // TCP
                writer.WriteUInt32(0);
            }
        );
        await Vxi11RecordFraming.WriteRecordAsync(stream, call, cts.Token);
        var reply = SkipReplyHeader(await Vxi11RecordFraming.ReadRecordAsync(stream, cts.Token));
        reply.ReadUInt32().ShouldBe((uint)port);

        await cts.CancelAsync();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task DeviceAbort_on_valid_lid_returns_NoError()
    {
        var (gateway, server, config, port, _) = BuildHarness();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        using var stream = tcp.GetStream();

        var createCall = BuildRpcCall(
            xid: 400,
            program: CoreProgram,
            version: CoreVersion,
            procedure: ProcCreateLink,
            body: writer =>
            {
                writer.WriteInt32(1);
                writer.WriteUInt32(0);
                writer.WriteUInt32(0);
                writer.WriteString("inst0");
            }
        );
        await Vxi11RecordFraming.WriteRecordAsync(stream, createCall, cts.Token);
        var createReply = SkipReplyHeader(
            await Vxi11RecordFraming.ReadRecordAsync(stream, cts.Token)
        );
        createReply.ReadInt32().ShouldBe(Vxi11NoError);
        var lid = createReply.ReadInt32();
        var abortPort = createReply.ReadUInt32();
        abortPort.ShouldBe((uint)port); // abort co-locates with core

        // Open a SEPARATE TCP connection (the abort channel) and send device_abort.
        using var abortTcp = new TcpClient();
        await abortTcp.ConnectAsync(IPAddress.Loopback, (int)abortPort, cts.Token);
        using var abortStream = abortTcp.GetStream();
        var abortCall = BuildRpcCall(
            xid: 401,
            program: AbortProgram,
            version: AbortVersion,
            procedure: ProcDeviceAbort,
            body: writer => writer.WriteInt32(lid)
        );
        await Vxi11RecordFraming.WriteRecordAsync(abortStream, abortCall, cts.Token);
        var abortReply = SkipReplyHeader(
            await Vxi11RecordFraming.ReadRecordAsync(abortStream, cts.Token)
        );
        abortReply.ReadInt32().ShouldBe(Vxi11NoError);

        await cts.CancelAsync();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task DeviceAbort_on_unknown_lid_returns_InvalidLink()
    {
        var (gateway, server, config, port, _) = BuildHarness();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        using var stream = tcp.GetStream();

        var abortCall = BuildRpcCall(
            xid: 500,
            program: AbortProgram,
            version: AbortVersion,
            procedure: ProcDeviceAbort,
            body: writer => writer.WriteInt32(424242) // never minted
        );
        await Vxi11RecordFraming.WriteRecordAsync(stream, abortCall, cts.Token);
        var reply = SkipReplyHeader(await Vxi11RecordFraming.ReadRecordAsync(stream, cts.Token));
        reply.ReadInt32().ShouldBe(Vxi11InvalidLink);

        await cts.CancelAsync();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task DeviceTrigger_lands_at_FakeBackend()
    {
        var (gateway, server, config, port, fake) = BuildHarness();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        using var stream = tcp.GetStream();

        var createCall = BuildRpcCall(
            xid: 600,
            program: CoreProgram,
            version: CoreVersion,
            procedure: ProcCreateLink,
            body: writer =>
            {
                writer.WriteInt32(1);
                writer.WriteUInt32(0);
                writer.WriteUInt32(0);
                writer.WriteString("inst0");
            }
        );
        await Vxi11RecordFraming.WriteRecordAsync(stream, createCall, cts.Token);
        var createReply = SkipReplyHeader(
            await Vxi11RecordFraming.ReadRecordAsync(stream, cts.Token)
        );
        createReply.ReadInt32().ShouldBe(Vxi11NoError);
        var lid = createReply.ReadInt32();
        _ = createReply.ReadUInt32();
        _ = createReply.ReadUInt32();

        var triggerCall = BuildRpcCall(
            xid: 601,
            program: CoreProgram,
            version: CoreVersion,
            procedure: ProcDeviceTrigger,
            body: writer =>
            {
                writer.WriteInt32(lid);
                writer.WriteInt32(0); // flags
                writer.WriteUInt32(1000);
                writer.WriteUInt32(0);
            }
        );
        await Vxi11RecordFraming.WriteRecordAsync(stream, triggerCall, cts.Token);
        var triggerReply = SkipReplyHeader(
            await Vxi11RecordFraming.ReadRecordAsync(stream, cts.Token)
        );
        triggerReply.ReadInt32().ShouldBe(Vxi11NoError);

        fake.TriggerCountFor(DeviceName.From("dut").ShouldBeOk()).ShouldBe(1);

        await cts.CancelAsync();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task Unknown_program_returns_PROG_UNAVAIL()
    {
        var (gateway, server, config, port, _) = BuildHarness();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        using var stream = tcp.GetStream();

        var call = BuildRpcCall(
            xid: 300,
            program: 1234567u,
            version: 1,
            procedure: 1,
            body: _ => { }
        );
        await Vxi11RecordFraming.WriteRecordAsync(stream, call, cts.Token);
        var reply = await Vxi11RecordFraming.ReadRecordAsync(stream, cts.Token);
        // verf flavor (4) + verf length (4) + accept_status (4) at offsets
        // 12, 16, 20 inside the reply. Cheaper to read with our reader.
        var reader = new Vxi11XdrCodec.XdrReader(reply);
        _ = reader.ReadUInt32(); // xid
        _ = reader.ReadUInt32(); // mtype
        _ = reader.ReadUInt32(); // reply_stat
        _ = reader.ReadUInt32(); // verf flavor
        _ = reader.ReadOpaque(); // verf body
        reader.ReadUInt32().ShouldBe(AcceptProgUnavail);

        await cts.CancelAsync();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException) { }
    }

    private static (
        Vxi11GatewayServer Gateway,
        IviCli.Domain.Servers.Server Server,
        ConfigDocument Config,
        int Port,
        FakeBackend Fake
    ) BuildHarness()
    {
        var port = GetFreePort();
        var deviceName = DeviceName.From("dut").ShouldBeOk();
        var device = new IviCli.Domain.Devices.Device(
            deviceName,
            VisaResource.Parse("TCPIP0::127.0.0.1::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );
        var serverName = ServerName.From("vxi-srv").ShouldBeOk();
        var endpoint = PublicEndpoint.From("inst0").ShouldBeOk();
        var bind = IpAddress.From("127.0.0.1").ShouldBeOk();
        var portValue = Port.From(port).ShouldBeOk();
        var srv = new IviCli.Domain.Servers.Server(serverName, ServerType.Vxi11, bind, portValue);
        var route = new Route(serverName, endpoint, deviceName);
        var config = ConfigDocument
            .Empty.AddDevice(device)
            .ShouldBeOk()
            .AddServer(srv)
            .ShouldBeOk()
            .AddRoute(route)
            .ShouldBeOk();
        var fake = new FakeBackend().ConfigureDevice(deviceName, "FAKE,VXI11,0,1.0");
        var gateway = new Vxi11GatewayServer(
            new FakeBackendFactory(fake),
            NullLogger<Vxi11GatewayServer>.Instance
        );
        return (gateway, srv, config, port, fake);
    }

    private static byte[] BuildRpcCall(
        uint xid,
        uint program,
        uint version,
        uint procedure,
        Action<Vxi11XdrCodec.XdrWriter> body
    )
    {
        var writer = new Vxi11XdrCodec.XdrWriter();
        writer.WriteUInt32(xid);
        writer.WriteUInt32(0); // CALL
        writer.WriteUInt32(2); // rpcvers
        writer.WriteUInt32(program);
        writer.WriteUInt32(version);
        writer.WriteUInt32(procedure);
        writer.WriteUInt32(0); // cred flavor (AUTH_NONE)
        writer.WriteOpaque([]); // cred body
        writer.WriteUInt32(0); // verf flavor (AUTH_NONE)
        writer.WriteOpaque([]); // verf body
        body(writer);
        return writer.ToArray();
    }

    private static Vxi11XdrCodec.XdrReader SkipReplyHeader(byte[] reply)
    {
        var reader = new Vxi11XdrCodec.XdrReader(reply);
        _ = reader.ReadUInt32(); // xid
        _ = reader.ReadUInt32(); // mtype
        _ = reader.ReadUInt32(); // reply_stat
        _ = reader.ReadUInt32(); // verf flavor
        _ = reader.ReadOpaque(); // verf body
        _ = reader.ReadUInt32(); // accept_stat
        return reader;
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
        throw new TimeoutException($"VXI-11 gateway did not bind to port {port}");
    }
}
