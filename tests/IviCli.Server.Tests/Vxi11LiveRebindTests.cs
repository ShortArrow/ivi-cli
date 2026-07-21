using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Text;
using IviCli.Backends.Fake;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;
using IviCli.Domain.Protocols;
using IviCli.Domain.Servers;
using IviCli.Domain.Session;
using IviCli.Domain.Visa;
using IviCli.Server.Vxi11;
using IviCli.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;
using static IviCli.Domain.Protocols.Vxi11Constants;

namespace IviCli.Server.Tests;

/// <summary>
/// A serving VXI-11 gateway must reflect a separate-process
/// <c>mock scenario activate</c> on the next write/read of an already-open
/// link — without the client re-creating the link or the gateway restarting.
/// </summary>
public sealed class Vxi11LiveRebindTests
{
    [Fact]
    public async Task Live_rebind_is_observed_mid_link()
    {
        var deviceName = DeviceName.From("dut").ShouldBeOk();
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

        var (server, config, port) = BuildHarness();
        var gateway = new Vxi11GatewayServer(
            new FakeBackendFactory(fake),
            NullLogger<Vxi11GatewayServer>.Instance,
            refresher
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        using var stream = tcp.GetStream();

        var lid = await CreateLinkAsync(stream, cts.Token);

        // Before: scenario "a".
        (await QueryAsync(stream, lid, xid: 200, "MEAS:VOLT?", cts.Token)).ShouldBe("1.00");

        // A separate process activates "b" (writes only the session store).
        await sessions.SaveAsync(SessionWith(deviceName, "b"), cts.Token);

        // After: the same link now serves scenario "b".
        (await QueryAsync(stream, lid, xid: 202, "MEAS:VOLT?", cts.Token)).ShouldBe("2.00");

        await cts.CancelAsync();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException) { }
    }

    private static async Task<int> CreateLinkAsync(NetworkStream stream, CancellationToken ct)
    {
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
        await Vxi11RecordFraming.WriteRecordAsync(stream, createCall, ct);
        var reply = SkipReplyHeader(await Vxi11RecordFraming.ReadRecordAsync(stream, ct));
        reply.ReadInt32().ShouldBe(Vxi11NoError);
        var lid = reply.ReadInt32();
        _ = reply.ReadUInt32(); // abort port
        _ = reply.ReadUInt32(); // maxRecvSize
        return lid;
    }

    private static async Task<string> QueryAsync(
        NetworkStream stream,
        int lid,
        uint xid,
        string scpi,
        CancellationToken ct
    )
    {
        var payload = Encoding.ASCII.GetBytes(scpi + "\n");
        var writeCall = BuildRpcCall(
            xid: xid,
            program: CoreProgram,
            version: CoreVersion,
            procedure: ProcDeviceWrite,
            body: writer =>
            {
                writer.WriteInt32(lid);
                writer.WriteUInt32(1000); // io timeout
                writer.WriteUInt32(0); // lock timeout
                writer.WriteInt32(WriteEndFlag);
                writer.WriteOpaque(payload);
            }
        );
        await Vxi11RecordFraming.WriteRecordAsync(stream, writeCall, ct);
        var writeReply = SkipReplyHeader(await Vxi11RecordFraming.ReadRecordAsync(stream, ct));
        writeReply.ReadInt32().ShouldBe(Vxi11NoError);
        _ = writeReply.ReadUInt32(); // bytes written

        var readCall = BuildRpcCall(
            xid: xid + 1,
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
        await Vxi11RecordFraming.WriteRecordAsync(stream, readCall, ct);
        var readReply = SkipReplyHeader(await Vxi11RecordFraming.ReadRecordAsync(stream, ct));
        readReply.ReadInt32().ShouldBe(Vxi11NoError);
        _ = readReply.ReadInt32(); // reason
        return Encoding.ASCII.GetString(readReply.ReadOpaque());
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
    ) BuildHarness()
    {
        var port = GetFreePort();
        var deviceName = DeviceName.From("dut").ShouldBeOk();
        var device = new Device(
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
        return (srv, config, port);
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
