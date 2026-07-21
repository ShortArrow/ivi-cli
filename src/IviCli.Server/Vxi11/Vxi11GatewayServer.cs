using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using IviCli.Application.Backends;
using IviCli.Application.Servers;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Protocols;
using IviCli.Domain.Scpi;
using IviCli.Domain.Servers;
using Microsoft.Extensions.Logging;
using static IviCli.Domain.Protocols.Vxi11Constants;

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
    private readonly IScenarioBindingRefresher _refresher;
    private readonly ILogger<Vxi11GatewayServer> _logger;
    private readonly ConcurrentDictionary<int, LinkState> _links = new();
    private int _linkCounter;

    /// <summary>Creates a new VXI-11 gateway.</summary>
    public Vxi11GatewayServer(
        IBackendFactory backendFactory,
        ILogger<Vxi11GatewayServer> logger,
        IScenarioBindingRefresher? refresher = null
    )
    {
        _backendFactory = backendFactory;
        _logger = logger;
        _refresher = refresher ?? NullScenarioBindingRefresher.Instance;
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

        // Per-connection ownership of lids in the shared link map.
        // The map itself is gateway-scoped (ADR 0029 §2a) so abort
        // requests on a separate TCP connection can find this link.
        var ownedLinks = new HashSet<int>();

        // Connection-scoped interrupt target — set by device_create_intr_chan,
        // shared across every link on this connection (ADR 0042).
        var connState = new ConnectionInterruptState();

        try
        {
            using var tcp = client;
            using var stream = tcp.GetStream();
            while (!ct.IsCancellationRequested)
            {
                byte[] body;
                try
                {
                    body = await Vxi11RecordFraming.ReadRecordAsync(stream, ct);
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
                    await HandleCoreAsync(
                        stream,
                        rpc,
                        body,
                        ownedLinks,
                        connState,
                        boundPort,
                        server,
                        config,
                        ct
                    );
                }
                else if (rpc.Program == AbortProgram)
                {
                    await HandleAbortAsync(stream, rpc, body, ct);
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
            foreach (var lid in ownedLinks)
            {
                if (_links.TryRemove(lid, out var state))
                {
                    try
                    {
                        _ = await state.Backend.CloseAsync(state.Device, CancellationToken.None);
                    }
                    catch
                    {
                        // Best-effort cleanup; channel is already tearing down.
                    }
                    state.Dispose();
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

        // Core + Abort co-locate on the same bound port (ADR 0029 §2a).
        var responsePort =
            queriedProgram == CoreProgram || queriedProgram == AbortProgram ? (uint)boundPort : 0u;
        var writer = new Vxi11XdrCodec.XdrWriter();
        writer.WriteUInt32(responsePort);
        await WriteAcceptedReplyAsync(stream, rpc.Xid, AcceptSuccess, writer.ToArray(), ct);
    }

    private async Task HandleAbortAsync(
        Stream stream,
        RpcCallHeader rpc,
        byte[] body,
        CancellationToken ct
    )
    {
        if (rpc.Version != AbortVersion)
        {
            await WriteAcceptedReplyAsync(stream, rpc.Xid, AcceptProgMismatch, null, ct);
            return;
        }
        if (rpc.Procedure != ProcDeviceAbort)
        {
            await WriteAcceptedReplyAsync(stream, rpc.Xid, AcceptProcUnavail, null, ct);
            return;
        }
        var reader = SkipRpcHeader(body);
        var lid = reader.ReadInt32();
        if (!_links.TryGetValue(lid, out var state))
        {
            await WriteErrorReplyAsync(stream, rpc.Xid, Vxi11InvalidLink, ct);
            return;
        }
        try
        {
            state.Cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Link disposed between lookup and cancel; treat as no-op.
        }
        _logger.LogInformation("VXI-11 device_abort signalled for link {Lid}", lid);
        await WriteErrorReplyAsync(stream, rpc.Xid, Vxi11NoError, ct);
    }

    private async Task HandleCoreAsync(
        Stream stream,
        RpcCallHeader rpc,
        byte[] body,
        HashSet<int> ownedLinks,
        ConnectionInterruptState connState,
        int boundPort,
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
                await DoCreateLinkAsync(
                    stream,
                    rpc.Xid,
                    procReader,
                    ownedLinks,
                    boundPort,
                    server,
                    config,
                    ct
                );
                break;
            case ProcDeviceWrite:
                await DoDeviceWriteAsync(stream, rpc.Xid, procReader, ct);
                break;
            case ProcDeviceRead:
                await DoDeviceReadAsync(stream, rpc.Xid, procReader, ct);
                break;
            case ProcDeviceClear:
                await DoDeviceClearAsync(stream, rpc.Xid, procReader, ct);
                break;
            case ProcDeviceTrigger:
                await DoDeviceTriggerAsync(stream, rpc.Xid, procReader, ct);
                break;
            case ProcCreateIntrChan:
                await DoCreateIntrChanAsync(stream, rpc.Xid, procReader, connState, ct);
                break;
            case ProcDestroyIntrChan:
                await DoDestroyIntrChanAsync(stream, rpc.Xid, connState, ct);
                break;
            case ProcDeviceEnableSrq:
                await DoDeviceEnableSrqAsync(stream, rpc.Xid, procReader, connState, ct);
                break;
            case ProcDestroyLink:
                await DoDestroyLinkAsync(stream, rpc.Xid, procReader, ownedLinks, ct);
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
        HashSet<int> ownedLinks,
        int boundPort,
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
        _links[lid] = new LinkState(backend, device);
        ownedLinks.Add(lid);
        await WriteCreateLinkReplyAsync(
            stream,
            xid,
            Vxi11NoError,
            lid,
            abortPort: (uint)boundPort,
            ct
        );
    }

    private async Task DoDeviceWriteAsync(
        Stream stream,
        uint xid,
        Vxi11XdrCodec.XdrReader reader,
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
        if (!_links.TryGetValue(parms.Lid, out var state))
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

        // Pick up an out-of-process scenario re-binding mid-link: a client
        // may hold one long-lived link while a separate `mock scenario
        // activate` runs. Refreshed per completed write so the change is
        // observed without re-creating the link. The refresher re-applies
        // only when the bound scenario name changed (scene state is
        // preserved otherwise), and is no-throw; guard anyway so a refresh
        // failure never kills the link.
        try
        {
            await _refresher.RefreshAsync(state.Device, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "scenario binding refresh failed; continuing");
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, state.Cts.Token);
        var opCt = linkedCts.Token;
        if (scpi.EndsWith('?'))
        {
            var queryResult = ScpiQuery.From(scpi);
            if (queryResult is not Result<ScpiQuery, ScpiError>.Ok { Value: var q })
            {
                await WriteWriteReplyAsync(stream, xid, Vxi11SyntaxError, size: 0, ct);
                return;
            }
            var resp = await state.Backend.QueryAsync(state.Device, q, opCt);
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
            var wrote = await state.Backend.WriteAsync(state.Device, c, opCt);
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

    private async Task DoDeviceReadAsync(
        Stream stream,
        uint xid,
        Vxi11XdrCodec.XdrReader reader,
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
        if (!_links.TryGetValue(parms.Lid, out var state))
        {
            await WriteReadReplyAsync(stream, xid, Vxi11InvalidLink, reason: 0, [], ct);
            return;
        }
        var data = state.PendingRead ?? [];
        state.PendingRead = null;
        // reason 4 = END flag set (whole message delivered)
        await WriteReadReplyAsync(stream, xid, Vxi11NoError, reason: 4, data, ct);
    }

    private async Task DoDeviceClearAsync(
        Stream stream,
        uint xid,
        Vxi11XdrCodec.XdrReader reader,
        CancellationToken ct
    )
    {
        var parms = new DeviceGenericParms(
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32()
        );
        if (!_links.TryGetValue(parms.Lid, out var state))
        {
            await WriteErrorReplyAsync(stream, xid, Vxi11InvalidLink, ct);
            return;
        }
        state.ClearPendingWrite();
        state.PendingRead = null;
        await WriteErrorReplyAsync(stream, xid, Vxi11NoError, ct);
    }

    private async Task DoDeviceTriggerAsync(
        Stream stream,
        uint xid,
        Vxi11XdrCodec.XdrReader reader,
        CancellationToken ct
    )
    {
        var parms = new DeviceGenericParms(
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32()
        );
        if (!_links.TryGetValue(parms.Lid, out var state))
        {
            await WriteErrorReplyAsync(stream, xid, Vxi11InvalidLink, ct);
            return;
        }
        // Forward the trigger to the backend. A BackendOperationNotSupported
        // result (typical for Socket / Replay) is mapped to Vxi11NotSupported
        // so the client gets the standard VXI-11 status code rather than a
        // soft success — operators see why the trigger didn't fire.
        var triggerResult = await state.Backend.TriggerAsync(state.Device, ct);
        var error =
            triggerResult is Result<Unit, BackendError>.Ok ? Vxi11NoError
            : ((Result<Unit, BackendError>.Error)triggerResult).Err is BackendOperationNotSupported
                ? Vxi11NotSupported
            : Vxi11IoError;
        await WriteErrorReplyAsync(stream, xid, error, ct);
    }

    private async Task DoCreateIntrChanAsync(
        Stream stream,
        uint xid,
        Vxi11XdrCodec.XdrReader reader,
        ConnectionInterruptState connState,
        CancellationToken ct
    )
    {
        var remote = Vxi11InterruptCodec.ReadRemoteFunc(ref reader);
        if (
            remote.ProgFamily != ProgFamilyTcp
            || remote.ProgNum != InterruptProgram
            || remote.ProgVers != InterruptVersion
        )
        {
            await WriteErrorReplyAsync(stream, xid, Vxi11NotSupported, ct);
            return;
        }
        connState.Target = remote;
        _logger.LogInformation(
            "VXI-11 device_create_intr_chan target {Host}:{Port}",
            remote.HostAddr,
            remote.HostPort
        );
        await WriteErrorReplyAsync(stream, xid, Vxi11NoError, ct);
    }

    private async Task DoDestroyIntrChanAsync(
        Stream stream,
        uint xid,
        ConnectionInterruptState connState,
        CancellationToken ct
    )
    {
        connState.Target = null;
        foreach (var lid in connState.ForwardingLinks.ToArray())
        {
            if (_links.TryGetValue(lid, out var state))
            {
                state.StopSrqForwarder();
            }
            connState.ForwardingLinks.Remove(lid);
        }
        await WriteErrorReplyAsync(stream, xid, Vxi11NoError, ct);
    }

    private async Task DoDeviceEnableSrqAsync(
        Stream stream,
        uint xid,
        Vxi11XdrCodec.XdrReader reader,
        ConnectionInterruptState connState,
        CancellationToken ct
    )
    {
        var parms = Vxi11InterruptCodec.ReadEnableSrqParms(ref reader);
        if (!_links.TryGetValue(parms.Lid, out var state))
        {
            await WriteErrorReplyAsync(stream, xid, Vxi11InvalidLink, ct);
            return;
        }
        if (!parms.Enable)
        {
            state.StopSrqForwarder();
            connState.ForwardingLinks.Remove(parms.Lid);
            await WriteErrorReplyAsync(stream, xid, Vxi11NoError, ct);
            return;
        }
        if (connState.Target is not { } target)
        {
            // Client asked to enable SRQ without first creating the
            // interrupt channel — VXI-11 OPERATION_NOT_SUPPORTED.
            await WriteErrorReplyAsync(stream, xid, Vxi11NotSupported, ct);
            return;
        }
        state.SrqHandle = parms.Handle;
        state.InterruptTarget = target;
        connState.ForwardingLinks.Add(parms.Lid);
        state.StartSrqForwarder(token => RunSrqForwarderAsync(state, target, token));
        await WriteErrorReplyAsync(stream, xid, Vxi11NoError, ct);
    }

    private async Task RunSrqForwarderAsync(
        LinkState state,
        DeviceRemoteFunc target,
        CancellationToken ct
    )
    {
        try
        {
            await foreach (var _ in state.Backend.ServiceRequestStream(state.Device, ct))
            {
                try
                {
                    await DeliverInterruptSrqAsync(target, state.SrqHandle, ct);
                }
                catch (Exception ex) when (ex is SocketException or IOException)
                {
                    _logger.LogWarning(
                        ex,
                        "VXI-11 device_intr_srq delivery to {Host}:{Port} failed; SRQ dropped",
                        target.HostAddr,
                        target.HostPort
                    );
                }
            }
        }
        catch (OperationCanceledException)
        { /* graceful shutdown */
        }
    }

    private static async Task DeliverInterruptSrqAsync(
        DeviceRemoteFunc target,
        byte[] handle,
        CancellationToken ct
    )
    {
        var ip = new IPAddress(
            System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(target.HostAddr)
        );
        using var client = new TcpClient();
        await client.ConnectAsync(ip, (int)target.HostPort, ct);
        using var stream = client.GetStream();

        var body = new Vxi11XdrCodec.XdrWriter();
        Vxi11InterruptCodec.WriteSrqParms(body, new DeviceSrqParms(handle));

        var rpc = new Vxi11XdrCodec.XdrWriter();
        rpc.WriteUInt32(0); // xid — server-initiated, client doesn't reply distinctively
        rpc.WriteUInt32(0); // mtype = CALL
        rpc.WriteUInt32(2); // rpcvers
        rpc.WriteUInt32(InterruptProgram);
        rpc.WriteUInt32(InterruptVersion);
        rpc.WriteUInt32(ProcDeviceIntrSrq);
        rpc.WriteUInt32(0); // cred flavor AUTH_NONE
        rpc.WriteOpaque(Array.Empty<byte>());
        rpc.WriteUInt32(0); // verf flavor
        rpc.WriteOpaque(Array.Empty<byte>());
        rpc.AppendRaw(body.ToArray());

        await Vxi11RecordFraming.WriteRecordAsync(stream, rpc.ToArray(), ct);
        // Per VXI-11 §B.7 the response is empty; we don't strictly need
        // to read it, but draining keeps the client side clean.
        try
        {
            _ = await Vxi11RecordFraming.ReadRecordAsync(stream, ct);
        }
        catch
        { /* swallow */
        }
    }

    private async Task DoDestroyLinkAsync(
        Stream stream,
        uint xid,
        Vxi11XdrCodec.XdrReader reader,
        HashSet<int> ownedLinks,
        CancellationToken ct
    )
    {
        var lid = reader.ReadInt32();
        if (!_links.TryRemove(lid, out var state))
        {
            await WriteErrorReplyAsync(stream, xid, Vxi11InvalidLink, ct);
            return;
        }
        ownedLinks.Remove(lid);
        _ = await state.Backend.CloseAsync(state.Device, ct);
        state.Dispose();
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
        await Vxi11RecordFraming.WriteRecordAsync(stream, writer.ToArray(), ct);
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

    /// <summary>
    /// Per-connection state for the VXI-11 Interrupt channel (ADR 0042).
    /// `Target` holds the most recent <c>device_create_intr_chan</c>
    /// payload; `ForwardingLinks` tracks which lids on this connection
    /// have an active forwarder so disconnect / destroy_intr_chan can
    /// stop them in bulk.
    /// </summary>
    private sealed class ConnectionInterruptState
    {
        public DeviceRemoteFunc? Target { get; set; }
        public HashSet<int> ForwardingLinks { get; } = new();
    }

    private sealed class LinkState : IDisposable
    {
        private readonly List<byte> _pendingWrite = new();
        private CancellationTokenSource? _srqForwarderCts;
        private Task? _srqForwarderTask;

        public LinkState(IIviBackend backend, Device device)
        {
            Backend = backend;
            Device = device;
            Cts = new CancellationTokenSource();
        }

        public IIviBackend Backend { get; }
        public Device Device { get; }
        public byte[]? PendingRead { get; set; }

        /// <summary>Cancellation source signalled by VXI-11 device_abort.</summary>
        public CancellationTokenSource Cts { get; }

        /// <summary>Remote host:port the client published via device_create_intr_chan (ADR 0042).</summary>
        public DeviceRemoteFunc? InterruptTarget { get; set; }

        /// <summary>Handle bytes echoed back to the client on every device_intr_srq delivery.</summary>
        public byte[] SrqHandle { get; set; } = Array.Empty<byte>();

        public byte[] AppendPendingWrite(ReadOnlySpan<byte> fragment)
        {
            _pendingWrite.AddRange(fragment.ToArray());
            return _pendingWrite.ToArray();
        }

        public void ClearPendingWrite() => _pendingWrite.Clear();

        /// <summary>Starts the background task forwarding SRQ events to the client.</summary>
        public void StartSrqForwarder(Func<CancellationToken, Task> runner)
        {
            StopSrqForwarder();
            _srqForwarderCts = new CancellationTokenSource();
            _srqForwarderTask = Task.Run(() => runner(_srqForwarderCts.Token));
        }

        /// <summary>Cancels and awaits the SRQ forwarder, if any.</summary>
        public void StopSrqForwarder()
        {
            try
            {
                _srqForwarderCts?.Cancel();
            }
            catch
            { /* swallow */
            }
            try
            {
                _srqForwarderTask?.Wait(TimeSpan.FromMilliseconds(200));
            }
            catch
            { /* swallow */
            }
            _srqForwarderCts?.Dispose();
            _srqForwarderCts = null;
            _srqForwarderTask = null;
        }

        public void Dispose()
        {
            StopSrqForwarder();
            Cts.Dispose();
        }
    }
}
