using System.Net;
using System.Net.Sockets;
using IviCli.Application.Backends;
using IviCli.Backends.Vxi11;
using IviCli.Domain.Devices;
using IviCli.Domain.Protocols;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;
using static IviCli.Domain.Protocols.Vxi11Constants;

namespace IviCli.Backends.Vxi11.Tests;

/// <summary>
/// Covers the real TCP/111 portmapper round-trip (issue #20). A real
/// instrument (e.g. Kikusui PWR801L) publishes its portmapper on TCP/111
/// and assigns the VXI-11 Core to a dynamic port, so the client must
/// GETPORT before connecting. When no portmapper answers, the client
/// falls back to the fixed port so co-located gateways keep working.
/// </summary>
public sealed class Vxi11PortmapperResolveTests
{
    [Fact]
    public async Task ResolveCorePortAsync_returns_core_port_from_getport_reply()
    {
        const int corePort = 54321;
        await using var pmap = StubHost.Start(
            (stream, ct) => ServePortmapperOnce(stream, corePort, ct)
        );

        var resolved = await Vxi11Portmapper.ResolveCorePortAsync(
            "127.0.0.1",
            pmap.Port,
            TimeSpan.FromSeconds(3),
            default
        );

        resolved.ShouldBe(corePort);
    }

    [Fact]
    public async Task ResolveCorePortAsync_throws_when_portmapper_unreachable()
    {
        var closedPort = FreePort();

        // Unreachable portmapper surfaces as a connection error (refused) or a
        // timeout (silently dropped). Either signal lets OpenAsync fall back.
        var ex = await Should.ThrowAsync<Exception>(
            Vxi11Portmapper.ResolveCorePortAsync(
                "127.0.0.1",
                closedPort,
                TimeSpan.FromSeconds(2),
                default
            )
        );

        (ex is SocketException or OperationCanceledException).ShouldBeTrue();
    }

    [Fact]
    public async Task OpenAsync_resolves_core_port_via_portmapper_then_connects()
    {
        await using var core = StubHost.Start(ServeCoreOpen);
        await using var pmap = StubHost.Start(
            (stream, ct) => ServePortmapperOnce(stream, core.Port, ct)
        );

        // Fixed fallback intentionally points at a dead port: success proves
        // the connection used the portmapper-resolved port, not the fallback.
        var backend = new Vxi11Backend(
            fallbackPort: FreePort(),
            portmapperPort: pmap.Port,
            usePortmapper: true
        );

        (await backend.OpenAsync(BuildDevice(), default)).ShouldBeOk();
    }

    [Fact]
    public async Task OpenAsync_falls_back_to_fixed_port_when_portmapper_unreachable()
    {
        await using var core = StubHost.Start(ServeCoreOpen);

        var backend = new Vxi11Backend(
            fallbackPort: core.Port,
            portmapperPort: FreePort(), // nothing listening → fall back
            usePortmapper: true
        );

        (await backend.OpenAsync(BuildDevice(), default)).ShouldBeOk();
    }

    private static Device BuildDevice() =>
        new(
            DeviceName.From("dut").ShouldBeOk(),
            VisaResource.Parse("TCPIP0::127.0.0.1::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Reads one GETPORT call and replies with <paramref name="corePort"/>.</summary>
    private static async Task ServePortmapperOnce(
        NetworkStream stream,
        int corePort,
        CancellationToken ct
    )
    {
        var (xid, proc) = await ReadCallAsync(stream, ct);
        proc.ShouldBe(PortmapGetPort);
        await WriteReplyAsync(stream, xid, w => w.WriteUInt32((uint)corePort), ct);
    }

    /// <summary>Serves create_link + interrupt-channel setup so OpenAsync succeeds.</summary>
    private static async Task ServeCoreOpen(NetworkStream stream, CancellationToken ct)
    {
        var create = await ReadCallAsync(stream, ct);
        create.Procedure.ShouldBe(ProcCreateLink);
        await WriteReplyAsync(
            stream,
            create.Xid,
            w =>
            {
                w.WriteInt32(Vxi11NoError);
                w.WriteInt32(7); // link id
                w.WriteUInt32(0); // abort port
                w.WriteUInt32(16 * 1024 * 1024); // maxRecvSize
            },
            ct
        );

        var intr = await ReadCallAsync(stream, ct);
        intr.Procedure.ShouldBe(ProcCreateIntrChan);
        await WriteReplyAsync(stream, intr.Xid, w => w.WriteInt32(Vxi11NoError), ct);

        var srq = await ReadCallAsync(stream, ct);
        srq.Procedure.ShouldBe(ProcDeviceEnableSrq);
        await WriteReplyAsync(stream, srq.Xid, w => w.WriteInt32(Vxi11NoError), ct);
    }

    private static async Task<(uint Xid, uint Procedure)> ReadCallAsync(
        NetworkStream stream,
        CancellationToken ct
    )
    {
        var bytes = await Vxi11RecordFraming.ReadRecordAsync(stream, ct);
        var reader = new Vxi11XdrCodec.XdrReader(bytes);
        var xid = reader.ReadUInt32();
        _ = reader.ReadUInt32(); // mtype = CALL
        _ = reader.ReadUInt32(); // rpcvers
        _ = reader.ReadUInt32(); // prog
        _ = reader.ReadUInt32(); // vers
        var proc = reader.ReadUInt32();
        return (xid, proc);
    }

    private static async Task WriteReplyAsync(
        NetworkStream stream,
        uint xid,
        Action<Vxi11XdrCodec.XdrWriter> body,
        CancellationToken ct
    )
    {
        var writer = new Vxi11XdrCodec.XdrWriter();
        writer.WriteUInt32(xid);
        writer.WriteUInt32(1); // mtype = REPLY
        writer.WriteUInt32(MsgAccepted);
        writer.WriteUInt32(0); // verf flavor
        writer.WriteOpaque([]); // verf body
        writer.WriteUInt32(AcceptSuccess);
        body(writer);
        await Vxi11RecordFraming.WriteRecordAsync(stream, writer.ToArray(), ct);
    }

    /// <summary>Single-accept loopback TCP listener that runs a serve callback.</summary>
    private sealed class StubHost : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _served;

        private StubHost(TcpListener listener, Func<NetworkStream, CancellationToken, Task> serve)
        {
            _listener = listener;
            _served = Task.Run(async () =>
            {
                using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                using var stream = client.GetStream();
                await serve(stream, _cts.Token);
            });
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public static StubHost Start(Func<NetworkStream, CancellationToken, Task> serve)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return new StubHost(listener, serve);
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            _listener.Stop();
            try
            {
                await _served;
            }
            catch
            { /* listener torn down */
            }
            _cts.Dispose();
        }
    }
}
