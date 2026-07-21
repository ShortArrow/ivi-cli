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

namespace IviCli.Server.HiSlip;

/// <summary>
/// Minimal HiSLIP-compatible gateway (PRD §7.2 / ADR 0007). v1 implements
/// the subset declared in ADR 0007 §1: Initialize / InitializeResponse,
/// AsyncInitialize / AsyncInitializeResponse, MaximumMessageSize negotiation,
/// synchronous Data + DataEnd for write/query, and FatalError on
/// unsupported feature. Locking, SRQ, async-IO cancellation, and trigger
/// remain out of scope.
/// </summary>
public sealed class HiSlipGatewayServer : IGatewayServer
{
    /// <summary>Protocol version advertised in the InitializeResponse.</summary>
    public const ushort ProtocolVersion = 0x0100; // HiSLIP 1.0

    /// <summary>The default max message size advertised by the server.</summary>
    public const ulong DefaultMaxMessageSize = 16 * 1024 * 1024; // 16 MiB

    private readonly IBackendFactory _backendFactory;
    private readonly IScenarioBindingRefresher _refresher;
    private readonly ILogger<HiSlipGatewayServer> _logger;
    private readonly object _lockGate = new();
    private ushort _lockHolder; // 0 = unlocked, otherwise the session id holding the lock

    // Sync→async correlation by session id: the sync handler stores the
    // opened backend / device pair after the handshake; the async handler
    // looks it up by the session id its AsyncInitialize carries (ADR 0041).
    private readonly ConcurrentDictionary<ushort, SessionBinding> _sessionBindings = new();

    /// <summary>Creates a new HiSLIP gateway.</summary>
    public HiSlipGatewayServer(
        IBackendFactory backendFactory,
        ILogger<HiSlipGatewayServer> logger,
        IScenarioBindingRefresher? refresher = null
    )
    {
        _backendFactory = backendFactory;
        _logger = logger;
        _refresher = refresher ?? NullScenarioBindingRefresher.Instance;
    }

    /// <inheritdoc/>
    public ServerType SupportedType => ServerType.HiSlip;

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

        _logger.LogInformation(
            "HiSLIP gateway listening on {Bind}:{Port} (server {Name})",
            server.Bind.Value,
            server.Port.Value,
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
                _ = HandleConnectionAsync(client, server, config, ct);
            }
        }
        finally
        {
            listener.Stop();
        }

        _logger.LogInformation("HiSLIP gateway stopped (server {Name})", server.Name.Value);
        return Result.Success<Unit, GatewayServerError>(Unit.Value);
    }

    private async Task HandleConnectionAsync(
        TcpClient client,
        Domain.Servers.Server server,
        ConfigDocument config,
        CancellationToken ct
    )
    {
        using var scope = _logger.BeginScope(
            new
            {
                Protocol = "hislip",
                RemoteEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown",
            }
        );

        try
        {
            using var tcp = client;
            using var stream = tcp.GetStream();

            // Read the first message header to determine sync vs async
            // channel (Initialize for sync, AsyncInitialize for async).
            var headerBuffer = new byte[HiSlipMessage.HeaderSize];
            await ReadExactlyAsync(stream, headerBuffer, ct);
            var firstHeader = HiSlipMessage.ReadHeader(headerBuffer);

            _logger.LogInformation("client connected; first message type {Type}", firstHeader.Type);

            switch (firstHeader.Type)
            {
                case HiSlipMessageType.Initialize:
                    await HandleSyncChannelAsync(stream, firstHeader, server, config, ct);
                    break;
                case HiSlipMessageType.AsyncInitialize:
                    // The client sends back the session id (low 16 bits of
                    // the message parameter) it received from
                    // InitializeResponse so the async channel can be
                    // correlated with its sync sibling.
                    var sessionId = (ushort)(firstHeader.MessageParameter & 0xFFFF);
                    await HandleAsyncChannelAsync(stream, firstHeader, sessionId, ct);
                    break;
                default:
                    await SendFatalAsync(
                        stream,
                        $"expected Initialize / AsyncInitialize, got {firstHeader.Type}",
                        ct
                    );
                    break;
            }

            _logger.LogInformation("client disconnected");
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown.
        }
        catch (EndOfStreamException ex)
            when (ex.Message.Contains("at 0/", StringComparison.Ordinal))
        {
            // The peer closed the TCP connection before sending any
            // HiSLIP handshake bytes. Most common cause: a Docker
            // HEALTHCHECK probe (`nc -z`) or a port scanner. Surface
            // it at Debug so production logs stay clean.
            _logger.LogDebug("HiSLIP probe disconnected without handshake");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HiSLIP connection terminated with unexpected error");
        }
    }

    private async Task HandleSyncChannelAsync(
        NetworkStream stream,
        HiSlipHeader init,
        Domain.Servers.Server server,
        ConfigDocument config,
        CancellationToken ct
    )
    {
        // Drain the Initialize payload — IVI-6.1 §10.2.1: the payload is
        // an ASCII string identifying the sub-protocol (typically the
        // LAN device name from the client's VISA resource, e.g.
        // "hislip0", "hislip1"). We use that to multiplex multiple
        // routed devices onto a single TCP port (issue #21).
        var initPayload = new byte[init.PayloadLength];
        if (initPayload.Length > 0)
        {
            await ReadExactlyAsync(stream, initPayload, ct);
        }
        var subAddress =
            initPayload.Length > 0
                ? System.Text.Encoding.ASCII.GetString(initPayload).Trim('\0')
                : string.Empty;

        // Resolve the bound device BEFORE replying so a sub-address
        // miss surfaces as a Fatal at handshake time (rather than
        // dragging the client through a successful InitializeResponse
        // followed by an unexplained close on first Data).
        Route? route = null;
        foreach (var r in config.Routes)
        {
            if (r.ServerName == server.Name && r.Endpoint.Value == subAddress)
            {
                route = r;
                break;
            }
        }
        var device = route is not null ? config.FindDevice(route.DeviceName) : null;
        if (device is null)
        {
            _logger.LogInformation(
                "HiSLIP Initialize miss: server {Server} has no route for sub-address {SubAddress}",
                server.Name.Value,
                subAddress
            );
            await SendFatalAsync(
                stream,
                $"no route for sub-address '{subAddress}' on server {server.Name.Value}",
                ct
            );
            return;
        }

        // Reply with InitializeResponse: protocol version + session id.
        // Phase 2 v1 uses sequential ushort session ids; v1 keeps it simple
        // with a single global counter (good enough for one-shot tests).
        var sessionId = (ushort)System.Threading.Interlocked.Increment(ref _sessionIdCounter);
        var respHeader = new byte[HiSlipMessage.HeaderSize];
        // Per spec: response control = overlap mode flag (0 = synchronous).
        // Response message parameter: hi-word = protocol version, lo-word = session id.
        var responseParameter = ((uint)ProtocolVersion << 16) | sessionId;
        HiSlipMessage.WriteHeader(
            respHeader,
            HiSlipMessageType.InitializeResponse,
            controlCode: 0,
            messageParameter: responseParameter,
            payloadLength: 0
        );
        await stream.WriteAsync(respHeader, ct);

        var backendResult = _backendFactory.CreateFor(device);
        if (backendResult is not Result<IIviBackend, BackendError>.Ok { Value: var backend })
        {
            await SendFatalAsync(stream, "backend resolution failed", ct);
            return;
        }

        var openResult = await backend.OpenAsync(device, ct);
        if (openResult is not Result<Unit, BackendError>.Ok)
        {
            await SendFatalAsync(stream, "backend open failed", ct);
            return;
        }

        // Publish the bound backend so an upcoming AsyncInitialize on this
        // session id can subscribe to ServiceRequestStream (ADR 0041).
        _sessionBindings[sessionId] = new SessionBinding(backend, device);

        try
        {
            var assembled = new StringBuilder();
            while (!ct.IsCancellationRequested)
            {
                await ReadExactlyAsync(
                    stream,
                    respHeader.AsMemory(0, HiSlipMessage.HeaderSize),
                    ct
                );
                var header = HiSlipMessage.ReadHeader(respHeader);

                if (header.Type is HiSlipMessageType.Data or HiSlipMessageType.DataEnd)
                {
                    var payload = new byte[header.PayloadLength];
                    if (payload.Length > 0)
                    {
                        await ReadExactlyAsync(stream, payload, ct);
                    }
                    assembled.Append(Encoding.ASCII.GetString(payload));
                    if (header.Type == HiSlipMessageType.DataEnd)
                    {
                        var scpi = assembled.ToString();
                        assembled.Clear();
                        // Echo the client's MessageId on the response per
                        // IVI-6.1 §10.6.2 (server-to-client Data carries the
                        // same MessageId as the client's initiating request).
                        await DispatchScpiAsync(
                            stream,
                            backend,
                            device,
                            scpi,
                            header.MessageParameter,
                            ct
                        );
                    }
                }
                else if (header.Type == HiSlipMessageType.Trigger)
                {
                    if (header.PayloadLength > 0)
                    {
                        var drain = new byte[header.PayloadLength];
                        await ReadExactlyAsync(stream, drain, ct);
                    }
                    var triggerResult = await backend.TriggerAsync(device, ct);
                    if (triggerResult is Result<Unit, BackendError>.Error triggerErr)
                    {
                        _logger.LogInformation(
                            "HiSLIP Trigger forwarded but backend declined: {Reason}",
                            triggerErr.Err.Message
                        );
                    }
                }
                else if (header.Type == HiSlipMessageType.FatalError)
                {
                    break;
                }
                else
                {
                    await SendFatalAsync(stream, $"unsupported sync type {header.Type}", ct);
                    break;
                }
            }
        }
        finally
        {
            _sessionBindings.TryRemove(sessionId, out _);
            _ = await backend.CloseAsync(device, ct);
        }
    }

    private async Task HandleAsyncChannelAsync(
        NetworkStream stream,
        HiSlipHeader init,
        ushort sessionId,
        CancellationToken ct
    )
    {
        // Drain async-init payload.
        if (init.PayloadLength > 0)
        {
            var buf = new byte[init.PayloadLength];
            await ReadExactlyAsync(stream, buf, ct);
        }

        // Echo back AsyncInitializeResponse. The message parameter carries
        // the server's protocol version + features (0 in v2).
        var resp = new byte[HiSlipMessage.HeaderSize];
        HiSlipMessage.WriteHeader(
            resp,
            HiSlipMessageType.AsyncInitializeResponse,
            controlCode: 0,
            messageParameter: 0,
            payloadLength: 0
        );
        await stream.WriteAsync(resp, ct);

        // Spawn the SRQ forwarder once the sync handler has published its
        // SessionBinding for this session id (ADR 0041). Time-bounded poll
        // so a client that opens the async channel before the sync
        // channel finishes its handshake does not deadlock.
        using var forwarderCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var forwarderTask = Task.Run(
            () => ForwardServiceRequestsAsync(stream, sessionId, forwarderCts.Token),
            forwarderCts.Token
        );

        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await ReadExactlyAsync(stream, resp.AsMemory(0, HiSlipMessage.HeaderSize), ct);
                }
                catch (EndOfStreamException)
                {
                    break;
                }
                var header = HiSlipMessage.ReadHeader(resp);
                byte[] payload = Array.Empty<byte>();
                if (header.PayloadLength > 0)
                {
                    payload = new byte[header.PayloadLength];
                    await ReadExactlyAsync(stream, payload, ct);
                }

                switch (header.Type)
                {
                    case HiSlipMessageType.AsyncMaximumMessageSize:
                        await SendMaximumMessageSizeResponseAsync(stream, ct);
                        break;
                    case HiSlipMessageType.AsyncLock:
                        // IVI-6.1 §10: AsyncLock with control byte 0 releases
                        // the lock, with 1 acquires. Single message type.
                        await HandleAsyncLockAsync(stream, header, sessionId, ct);
                        break;
                    case HiSlipMessageType.AsyncDeviceClear:
                        await SendDeviceClearAckAsync(stream, ct);
                        break;
                    case HiSlipMessageType.FatalError:
                        return;
                    default:
                        // Unknown async control: silently ignored in v2.
                        break;
                }
            }
        }
        finally
        {
            // Release any lock this session was holding on disconnect.
            ReleaseLock(sessionId);
            try
            {
                forwarderCts.Cancel();
                await forwarderTask;
            }
            catch
            { /* swallow */
            }
        }
    }

    private async Task ForwardServiceRequestsAsync(
        NetworkStream stream,
        ushort sessionId,
        CancellationToken ct
    )
    {
        // Wait up to 2 s for the sync handler to publish the binding.
        SessionBinding? binding = null;
        for (var i = 0; i < 40 && !ct.IsCancellationRequested; i++)
        {
            if (_sessionBindings.TryGetValue(sessionId, out binding))
            {
                break;
            }
            try
            {
                await Task.Delay(50, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
        if (binding is null)
        {
            return;
        }

        try
        {
            await foreach (var srq in binding.Backend.ServiceRequestStream(binding.Device, ct))
            {
                var header = new byte[HiSlipMessage.HeaderSize];
                HiSlipMessage.WriteHeader(
                    header,
                    HiSlipMessageType.ServiceRequest,
                    controlCode: srq.StatusByte,
                    messageParameter: 0,
                    payloadLength: 0
                );
                try
                {
                    await stream.WriteAsync(header, ct);
                }
                catch (Exception ex) when (ex is SocketException or IOException)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        { /* graceful shutdown */
        }
    }

    private sealed record SessionBinding(IIviBackend Backend, Device Device);

    private static async Task SendMaximumMessageSizeResponseAsync(
        NetworkStream stream,
        CancellationToken ct
    )
    {
        var resp = new byte[HiSlipMessage.HeaderSize];
        var payload = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(payload, DefaultMaxMessageSize);
        HiSlipMessage.WriteHeader(
            resp,
            HiSlipMessageType.AsyncMaximumMessageSizeResponse,
            controlCode: 0,
            messageParameter: 0,
            payloadLength: 8
        );
        await stream.WriteAsync(resp, ct);
        await stream.WriteAsync(payload, ct);
    }

    private async Task HandleAsyncLockAsync(
        NetworkStream stream,
        HiSlipHeader request,
        ushort sessionId,
        CancellationToken ct
    )
    {
        // Control byte semantics (ADR 0007 §1.5): 1 = acquire, 0 = release.
        // Spec proper uses a flag bit; for this project's v2 minimum we accept
        // anything non-zero as acquire to match common VISA clients.
        var acquire = request.ControlCode != 0;
        byte responseControl;
        if (!acquire)
        {
            ReleaseLock(sessionId);
            responseControl = 1; // granted (release always succeeds)
        }
        else
        {
            // IVI-6.1 §10 carries lock_timeout (ms) in MessageParameter.
            // Zero is "fail immediately if contended" — original v2 behaviour.
            // Non-zero polls _lockHolder every 50 ms until the deadline.
            var timeoutMs = request.MessageParameter;
            responseControl = await TryAcquireLockAsync(sessionId, timeoutMs, ct);
        }
        var resp = new byte[HiSlipMessage.HeaderSize];
        HiSlipMessage.WriteHeader(
            resp,
            HiSlipMessageType.AsyncLockResponse,
            controlCode: responseControl,
            messageParameter: 0,
            payloadLength: 0
        );
        await stream.WriteAsync(resp, ct);
    }

    private void ReleaseLock(ushort sessionId)
    {
        lock (_lockGate)
        {
            if (_lockHolder == sessionId)
            {
                _lockHolder = 0;
            }
        }
    }

    private async Task<byte> TryAcquireLockAsync(
        ushort sessionId,
        uint timeoutMs,
        CancellationToken ct
    )
    {
        const int PollIntervalMs = 50;
        var deadline = timeoutMs == 0 ? (long?)null : Environment.TickCount64 + timeoutMs;
        while (true)
        {
            lock (_lockGate)
            {
                if (_lockHolder == 0 || _lockHolder == sessionId)
                {
                    _lockHolder = sessionId;
                    return 1;
                }
            }
            if (deadline is null || Environment.TickCount64 >= deadline.Value)
            {
                return 0;
            }
            var remaining = (int)Math.Min(PollIntervalMs, deadline.Value - Environment.TickCount64);
            if (remaining <= 0)
            {
                return 0;
            }
            try
            {
                await Task.Delay(remaining, ct);
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
        }
    }

    private static async Task SendDeviceClearAckAsync(NetworkStream stream, CancellationToken ct)
    {
        var resp = new byte[HiSlipMessage.HeaderSize];
        HiSlipMessage.WriteHeader(
            resp,
            HiSlipMessageType.AsyncDeviceClearAcknowledge,
            controlCode: 0,
            messageParameter: 0,
            payloadLength: 0
        );
        await stream.WriteAsync(resp, ct);
    }

    private async Task DispatchScpiAsync(
        NetworkStream stream,
        IIviBackend backend,
        Domain.Devices.Device device,
        string scpi,
        uint clientMessageId,
        CancellationToken ct
    )
    {
        // Pick up an out-of-process scenario re-binding mid-link: a client
        // may hold one long-lived link while a separate `mock scenario
        // activate` runs. Refreshed per message so the change is observed
        // without re-creating the link. The refresher re-applies only when
        // the bound scenario name changed (scene state is preserved
        // otherwise), and is no-throw; guard anyway so a refresh failure
        // never kills the link.
        try
        {
            await _refresher.RefreshAsync(device, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "scenario binding refresh failed; continuing");
        }

        // Real VISA clients (NI-VISA, Keysight, R&S, PyVISA-py) terminate
        // SCPI lines with `\r\n` or `\n` per IEEE 488.2 §7.5. Backends and
        // scenario matchers see canonical, terminator-free strings.
        var normalized = scpi.TrimEnd('\r', '\n');
        if (normalized.EndsWith('?'))
        {
            var queryResult = ScpiQuery.From(normalized);
            if (queryResult is not Result<ScpiQuery, ScpiError>.Ok { Value: var q })
            {
                await SendFatalAsync(stream, "invalid SCPI query", ct);
                return;
            }
            var resp = await backend.QueryAsync(device, q, ct);
            if (resp is Result<string, BackendError>.Ok { Value: var responseText })
            {
                await SendDataEndAsync(stream, responseText, clientMessageId, ct);
            }
            else
            {
                await SendFatalAsync(stream, "backend query failed", ct);
            }
        }
        else
        {
            var cmdResult = ScpiCommand.From(normalized);
            if (cmdResult is not Result<ScpiCommand, ScpiError>.Ok { Value: var c })
            {
                await SendFatalAsync(stream, "invalid SCPI command", ct);
                return;
            }
            _ = await backend.WriteAsync(device, c, ct);
            // No response for a write in SCPI; HiSLIP clients don't expect one either.
        }
    }

    private static async Task SendDataEndAsync(
        NetworkStream stream,
        string responseText,
        uint clientMessageId,
        CancellationToken ct
    )
    {
        var bytes = Encoding.ASCII.GetBytes(responseText);
        var header = new byte[HiSlipMessage.HeaderSize];
        HiSlipMessage.WriteHeader(
            header,
            HiSlipMessageType.DataEnd,
            controlCode: 0,
            messageParameter: clientMessageId,
            payloadLength: (ulong)bytes.Length
        );
        await stream.WriteAsync(header, ct);
        if (bytes.Length > 0)
        {
            await stream.WriteAsync(bytes, ct);
        }
    }

    private static async Task SendFatalAsync(
        NetworkStream stream,
        string reason,
        CancellationToken ct
    )
    {
        var payload = Encoding.ASCII.GetBytes(reason);
        var header = new byte[HiSlipMessage.HeaderSize];
        HiSlipMessage.WriteHeader(
            header,
            HiSlipMessageType.FatalError,
            controlCode: 0,
            messageParameter: 0,
            payloadLength: (ulong)payload.Length
        );
        await stream.WriteAsync(header, ct);
        if (payload.Length > 0)
        {
            await stream.WriteAsync(payload, ct);
        }
    }

    private static async Task ReadExactlyAsync(
        NetworkStream stream,
        byte[] buffer,
        CancellationToken ct
    )
    {
        await ReadExactlyAsync(stream, buffer.AsMemory(), ct);
    }

    private static async Task ReadExactlyAsync(
        NetworkStream stream,
        Memory<byte> buffer,
        CancellationToken ct
    )
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], ct);
            if (read <= 0)
            {
                throw new EndOfStreamException(
                    $"HiSLIP stream closed early at {offset}/{buffer.Length}"
                );
            }
            offset += read;
        }
    }

    private int _sessionIdCounter;
}
