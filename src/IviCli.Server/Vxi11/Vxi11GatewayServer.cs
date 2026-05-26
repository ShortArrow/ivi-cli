using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using IviCli.Application.Backends;
using IviCli.Application.Servers;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Servers;
using Microsoft.Extensions.Logging;
using static IviCli.Server.Vxi11.Vxi11Constants;

namespace IviCli.Server.Vxi11;

/// <summary>
/// Minimum-viable VXI-11 gateway covering the procedures PRD §6.2 needs
/// (create_link / device_write / device_read / device_clear /
/// destroy_link) plus a co-located portmapper GETPORT. Per-connection
/// task handles one VISA session, routes SCPI to the configured backend,
/// and replies on the same TCP socket. The ONC RPC envelope, XDR
/// primitives, and record-marking framing are hand-rolled — no
/// third-party RPC dependency.
/// </summary>
public sealed class Vxi11GatewayServer : IGatewayServer
{
    private readonly IBackendFactory _backendFactory;
    private readonly ILogger<Vxi11GatewayServer> _logger;
    private int _linkCounter;

    /// <summary>Creates a new VXI-11 gateway.</summary>
    public Vxi11GatewayServer(IBackendFactory backendFactory, ILogger<Vxi11GatewayServer> logger)
    {
        _backendFactory = backendFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public ServerType SupportedType => ServerType.Vxi11;

    /// <inheritdoc/>
    public async Task<Result<Unit, GatewayServerError>> RunAsync(
        Domain.Servers.Server server,
        ConfigDocument config,
        CancellationToken ct
    )
    {
        if (!IPAddress.TryParse(server.Bind.Value, out var bindAddr))
        {
            bindAddr = IPAddress.Loopback;
        }
        var listener = new TcpListener(bindAddr, server.Port.Value);
        try
        {
            listener.Start();
        }
        catch (SocketException ex)
        {
            return Result.Failure<Unit, GatewayServerError>(
                new GatewayBindFailure(server.Bind, server.Port, ex.Message, ex)
            );
        }

        var actualPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        _logger.LogInformation(
            "VXI-11 gateway listening on {Bind}:{Port} (server {Name})",
            server.Bind.Value,
            actualPort,
            server.Name.Value
        );

        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                _ = HandleConnectionAsync(client, server, config, actualPort, ct);
            }
        }
        finally
        {
            listener.Stop();
        }

        _logger.LogInformation("VXI-11 gateway stopped (server {Name})", server.Name.Value);
        return Result.Success<Unit, GatewayServerError>(Unit.Value);
    }

    private async Task HandleConnectionAsync(
        TcpClient client,
        Domain.Servers.Server server,
        ConfigDocument config,
        int boundPort,
        CancellationToken ct
    )
    {
        using var scope = _logger.BeginScope(
            new
            {
                Protocol = "vxi11",
                RemoteEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown",
            }
        );

        // Per-connection link state. v1 supports one link per session;
        // additional create_link calls just overwrite the previous link
        // after closing the prior backend session.
        var links = new ConcurrentDictionary<int, LinkState>();

        try
        {
            using var tcp = client;
            using var stream = tcp.GetStream();
            while (!ct.IsCancellationRequested)
            {
                byte[] body;
                try
                {
                    body = await Vxi11XdrCodec.ReadRecordAsync(stream, ct);
                }
                catch (EndOfStreamException)
                {
                    break;
                }
                var reader = new Vxi11XdrCodec.XdrReader(body);
                var rpc = DecodeRpcCall(ref reader);

                if (rpc.Program == PortmapProgram)
                {
                    await HandlePortmapAsync(stream, rpc, reader, boundPort, ct);
                }
                else if (rpc.Program == CoreProgram)
                {
                    await HandleCoreAsync(stream, rpc, body, links, server, config, ct);
                }
                else
                {
                    await WriteAcceptedReplyAsync(stream, rpc.Xid, AcceptProgUnavail, null, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VXI-11 connection terminated with unexpected error");
        }
        finally
        {
            foreach (var (_, state) in links)
            {
                try
                {
                    _ = await state.Backend.CloseAsync(state.Device, CancellationToken.None);
                }
                catch
                {
                    // Best-effort cleanup; channel is already tearing down.
                }
            }
        }
    }

    private static RpcCallHeader DecodeRpcCall(ref Vxi11XdrCodec.XdrReader reader)
    {
        var xid = reader.ReadUInt32();
        var mtype = reader.ReadUInt32();
        if (mtype != 0)
        {
            throw new InvalidDataException("expected RPC CALL (mtype=0)");
        }
        var rpcvers = reader.ReadUInt32();
        if (rpcvers != 2)
        {
            throw new InvalidDataException($"unsupported RPC version {rpcvers}");
        }
        var prog = reader.ReadUInt32();
        var vers = reader.ReadUInt32();
        var proc = reader.ReadUInt32();
        // cred + verf: each is (flavor u32, opaque body). v1 only honours AUTH_NONE.
        _ = reader.ReadUInt32(); // cred flavor
        _ = reader.ReadOpaque(); // cred body
        _ = reader.ReadUInt32(); // verf flavor
        _ = reader.ReadOpaque(); // verf body
        return new RpcCallHeader(xid, prog, vers, proc);
    }

    private static async Task HandlePortmapAsync(
        Stream stream,
        RpcCallHeader rpc,
        Vxi11XdrCodec.XdrReader reader,
        int boundPort,
        CancellationToken ct
    )
    {
        if (rpc.Version != PortmapVersion || rpc.Procedure != PortmapGetPort)
        {
            await WriteAcceptedReplyAsync(stream, rpc.Xid, AcceptProcUnavail, null, ct);
            return;
        }
        var queriedProgram = reader.ReadUInt32();
        _ = reader.ReadUInt32(); // version
        _ = reader.ReadUInt32(); // protocol (6 = TCP)
        _ = reader.ReadUInt32(); // port (ignored on lookup)

        var responsePort = queriedProgram == CoreProgram ? (uint)boundPort : 0u;
        var writer = new Vxi11XdrCodec.XdrWriter();
        writer.WriteUInt32(responsePort);
        await WriteAcceptedReplyAsync(stream, rpc.Xid, AcceptSuccess, writer.ToArray(), ct);
    }

    private async Task HandleCoreAsync(
        Stream stream,
        RpcCallHeader rpc,
        byte[] body,
        ConcurrentDictionary<int, LinkState> links,
        Domain.Servers.Server server,
        ConfigDocument config,
        CancellationToken ct
    )
    {
        if (rpc.Version != CoreVersion)
        {
            await WriteAcceptedReplyAsync(stream, rpc.Xid, AcceptProgMismatch, null, ct);
            return;
        }

        // Re-create a reader positioned at the procedure body. Skipping
        // the RPC header bytes is cleaner here than threading the reader
        // through HandleConnectionAsync because each procedure decodes
        // its own argument structure.
        var procReader = SkipRpcHeader(body);

        switch (rpc.Procedure)
        {
            case ProcCreateLink:
                await DoCreateLinkAsync(stream, rpc.Xid, procReader, links, server, config, ct);
                break;
            case ProcDeviceWrite:
                await DoDeviceWriteAsync(stream, rpc.Xid, procReader, links, ct);
                break;
            case ProcDeviceRead:
                await DoDeviceReadAsync(stream, rpc.Xid, procReader, links, ct);
                break;
            case ProcDeviceClear:
                await DoDeviceClearAsync(stream, rpc.Xid, procReader, links, ct);
                break;
            case ProcDestroyLink:
                await DoDestroyLinkAsync(stream, rpc.Xid, procReader, links, ct);
                break;
            default:
                await WriteAcceptedReplyAsync(stream, rpc.Xid, AcceptProcUnavail, null, ct);
                break;
        }
    }

    private async Task DoCreateLinkAsync(
        Stream stream,
        uint xid,
        Vxi11XdrCodec.XdrReader reader,
        ConcurrentDictionary<int, LinkState> links,
        Domain.Servers.Server server,
        ConfigDocument config,
        CancellationToken ct
    )
    {
        var parms = new CreateLinkParms(
            reader.ReadInt32(),
            reader.ReadUInt32() != 0,
            reader.ReadUInt32(),
            reader.ReadString()
        );
        _ = parms.Device; // device hint ignored; routes select the device

        Route? route = null;
        foreach (var r in config.Routes)
        {
            if (r.ServerName == server.Name)
            {
                route = r;
                break;
            }
        }
        var device = route is not null ? config.FindDevice(route.DeviceName) : null;
        if (device is null)
        {
            await WriteCreateLinkReplyAsync(stream, xid, Vxi11IoError, lid: 0, abortPort: 0, ct);
            return;
        }

        var backendResult = _backendFactory.CreateFor(device);
        if (backendResult is not Result<IIviBackend, BackendError>.Ok { Value: var backend })
        {
            await WriteCreateLinkReplyAsync(stream, xid, Vxi11IoError, lid: 0, abortPort: 0, ct);
            return;
        }
        var openResult = await backend.OpenAsync(device, ct);
        if (openResult is not Result<Unit, BackendError>.Ok)
        {
            await WriteCreateLinkReplyAsync(stream, xid, Vxi11IoError, lid: 0, abortPort: 0, ct);
            return;
        }

        var lid = System.Threading.Interlocked.Increment(ref _linkCounter);
        links[lid] = new LinkState(backend, device);
        await WriteCreateLinkReplyAsync(stream, xid, Vxi11NoError, lid, abortPort: 0, ct);
    }

    private static async Task DoDeviceWriteAsync(
        Stream stream,
        uint xid,
        Vxi11XdrCodec.XdrReader reader,
        ConcurrentDictionary<int, LinkState> links,
        CancellationToken ct
    )
    {
        var parms = new DeviceWriteParms(
            reader.ReadInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadInt32(),
            reader.ReadOpaque()
        );
        if (!links.TryGetValue(parms.Lid, out var state))
        {
            await WriteWriteReplyAsync(stream, xid, Vxi11InvalidLink, size: 0, ct);
            return;
        }
        var pendingWrite = state.AppendPendingWrite(parms.Data);
        if ((parms.Flags & WriteEndFlag) == 0)
        {
            // Mid-stream fragment: stash and ack the bytes without dispatch.
            await WriteWriteReplyAsync(stream, xid, Vxi11NoError, (uint)parms.Data.Length, ct);
            return;
        }
        var scpi = Encoding.ASCII.GetString(pendingWrite).TrimEnd('\r', '\n');
        state.ClearPendingWrite();
        if (scpi.EndsWith('?'))
        {
            var queryResult = ScpiQuery.From(scpi);
            if (queryResult is not Result<ScpiQuery, ScpiError>.Ok { Value: var q })
            {
                await WriteWriteReplyAsync(stream, xid, Vxi11SyntaxError, size: 0, ct);
                return;
            }
            var resp = await state.Backend.QueryAsync(state.Device, q, ct);
            if (resp is Result<string, BackendError>.Ok { Value: var responseText })
            {
                state.PendingRead = Encoding.ASCII.GetBytes(responseText);
                await WriteWriteReplyAsync(stream, xid, Vxi11NoError, (uint)parms.Data.Length, ct);
            }
            else
            {
                await WriteWriteReplyAsync(stream, xid, Vxi11IoError, size: 0, ct);
            }
        }
        else
        {
            var cmdResult = ScpiCommand.From(scpi);
            if (cmdResult is not Result<ScpiCommand, ScpiError>.Ok { Value: var c })
            {
                await WriteWriteReplyAsync(stream, xid, Vxi11SyntaxError, size: 0, ct);
                return;
            }
            var wrote = await state.Backend.WriteAsync(state.Device, c, ct);
            if (wrote is Result<Unit, BackendError>.Ok)
            {
                await WriteWriteReplyAsync(stream, xid, Vxi11NoError, (uint)parms.Data.Length, ct);
            }
            else
            {
                await WriteWriteReplyAsync(stream, xid, Vxi11IoError, size: 0, ct);
            }
        }
    }

    private static async Task DoDeviceReadAsync(
        Stream stream,
        uint xid,
        Vxi11XdrCodec.XdrReader reader,
        ConcurrentDictionary<int, LinkState> links,
        CancellationToken ct
    )
    {
        var parms = new DeviceReadParms(
            reader.ReadInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadInt32(),
            (byte)(reader.ReadUInt32() & 0xFF)
        );
        if (!links.TryGetValue(parms.Lid, out var state))
        {
            await WriteReadReplyAsync(stream, xid, Vxi11InvalidLink, reason: 0, [], ct);
            return;
        }
        var data = state.PendingRead ?? [];
        state.PendingRead = null;
        // reason 4 = END flag set (whole message delivered)
        await WriteReadReplyAsync(stream, xid, Vxi11NoError, reason: 4, data, ct);
    }

    private static async Task DoDeviceClearAsync(
        Stream stream,
        uint xid,
        Vxi11XdrCodec.XdrReader reader,
        ConcurrentDictionary<int, LinkState> links,
        CancellationToken ct
    )
    {
        var parms = new DeviceGenericParms(
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32()
        );
        if (!links.TryGetValue(parms.Lid, out var state))
        {
            await WriteErrorReplyAsync(stream, xid, Vxi11InvalidLink, ct);
            return;
        }
        state.ClearPendingWrite();
        state.PendingRead = null;
        await WriteErrorReplyAsync(stream, xid, Vxi11NoError, ct);
    }

    private static async Task DoDestroyLinkAsync(
        Stream stream,
        uint xid,
        Vxi11XdrCodec.XdrReader reader,
        ConcurrentDictionary<int, LinkState> links,
        CancellationToken ct
    )
    {
        var lid = reader.ReadInt32();
        if (!links.TryRemove(lid, out var state))
        {
            await WriteErrorReplyAsync(stream, xid, Vxi11InvalidLink, ct);
            return;
        }
        _ = await state.Backend.CloseAsync(state.Device, ct);
        await WriteErrorReplyAsync(stream, xid, Vxi11NoError, ct);
    }

    private static Vxi11XdrCodec.XdrReader SkipRpcHeader(byte[] body)
    {
        // 4 (xid) + 4 (mtype) + 4 (rpcvers) + 4 (prog) + 4 (vers) + 4 (proc) = 24
        // + cred flavor (4) + cred opaque (4 length + 0 body for AUTH_NONE) = 32
        // + verf flavor (4) + verf opaque (4) = 40
        // The opaque-body lengths can be non-zero in principle; for robustness
        // re-read the header here to compute the precise procedure-body offset.
        var probe = new Vxi11XdrCodec.XdrReader(body);
        _ = probe.ReadUInt32(); // xid
        _ = probe.ReadUInt32(); // mtype
        _ = probe.ReadUInt32(); // rpcvers
        _ = probe.ReadUInt32(); // prog
        _ = probe.ReadUInt32(); // vers
        _ = probe.ReadUInt32(); // proc
        _ = probe.ReadUInt32(); // cred flavor
        _ = probe.ReadOpaque(); // cred body
        _ = probe.ReadUInt32(); // verf flavor
        _ = probe.ReadOpaque(); // verf body
        return new Vxi11XdrCodec.XdrReader(body.AsMemory(probe.Position));
    }

    private static async Task WriteAcceptedReplyAsync(
        Stream stream,
        uint xid,
        uint acceptStatus,
        byte[]? procedureBody,
        CancellationToken ct
    )
    {
        var writer = new Vxi11XdrCodec.XdrWriter();
        writer.WriteUInt32(xid);
        writer.WriteUInt32(1); // mtype = REPLY
        writer.WriteUInt32(MsgAccepted);
        // verf: AUTH_NONE (flavor 0, length 0)
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(acceptStatus);
        if (procedureBody is not null)
        {
            writer.AppendRaw(procedureBody);
        }
        await Vxi11XdrCodec.WriteRecordAsync(stream, writer.ToArray(), ct);
    }

    private static async Task WriteCreateLinkReplyAsync(
        Stream stream,
        uint xid,
        int error,
        int lid,
        uint abortPort,
        CancellationToken ct
    )
    {
        var inner = new Vxi11XdrCodec.XdrWriter();
        inner.WriteInt32(error);
        inner.WriteInt32(lid);
        inner.WriteUInt32(abortPort); // abortPort, padded to 32-bit
        inner.WriteUInt32(16 * 1024 * 1024); // maxRecvSize advertised to client
        await WriteAcceptedReplyAsync(stream, xid, AcceptSuccess, inner.ToArray(), ct);
    }

    private static async Task WriteWriteReplyAsync(
        Stream stream,
        uint xid,
        int error,
        uint size,
        CancellationToken ct
    )
    {
        var inner = new Vxi11XdrCodec.XdrWriter();
        inner.WriteInt32(error);
        inner.WriteUInt32(size);
        await WriteAcceptedReplyAsync(stream, xid, AcceptSuccess, inner.ToArray(), ct);
    }

    private static async Task WriteReadReplyAsync(
        Stream stream,
        uint xid,
        int error,
        int reason,
        byte[] data,
        CancellationToken ct
    )
    {
        var inner = new Vxi11XdrCodec.XdrWriter();
        inner.WriteInt32(error);
        inner.WriteInt32(reason);
        inner.WriteOpaque(data);
        await WriteAcceptedReplyAsync(stream, xid, AcceptSuccess, inner.ToArray(), ct);
    }

    private static async Task WriteErrorReplyAsync(
        Stream stream,
        uint xid,
        int error,
        CancellationToken ct
    )
    {
        var inner = new Vxi11XdrCodec.XdrWriter();
        inner.WriteInt32(error);
        await WriteAcceptedReplyAsync(stream, xid, AcceptSuccess, inner.ToArray(), ct);
    }

    private sealed class LinkState
    {
        private readonly List<byte> _pendingWrite = new();

        public LinkState(IIviBackend backend, Device device)
        {
            Backend = backend;
            Device = device;
        }

        public IIviBackend Backend { get; }
        public Device Device { get; }
        public byte[]? PendingRead { get; set; }

        public byte[] AppendPendingWrite(ReadOnlySpan<byte> fragment)
        {
            _pendingWrite.AddRange(fragment.ToArray());
            return _pendingWrite.ToArray();
        }

        public void ClearPendingWrite() => _pendingWrite.Clear();
    }
}
