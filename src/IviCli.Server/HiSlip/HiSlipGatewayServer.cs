using System.Net;
using System.Net.Sockets;
using System.Text;
using IviCli.Application.Backends;
using IviCli.Application.Servers;
using IviCli.Domain;
using IviCli.Domain.Configuration;
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
    private readonly ILogger<HiSlipGatewayServer> _logger;
    private readonly object _lockGate = new();
    private ushort _lockHolder; // 0 = unlocked, otherwise the session id holding the lock

    /// <summary>Creates a new HiSLIP gateway.</summary>
    public HiSlipGatewayServer(IBackendFactory backendFactory, ILogger<HiSlipGatewayServer> logger)
    {
        _backendFactory = backendFactory;
        _logger = logger;
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
        // Drain the Initialize payload (sub-protocol name and client identifier).
        var initPayload = new byte[init.PayloadLength];
        if (initPayload.Length > 0)
        {
            await ReadExactlyAsync(stream, initPayload, ct);
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

        // Resolve the bound device. HiSLIP servers in v1 expose one logical
        // instrument; we pick the first route on this server.
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
            await SendFatalAsync(stream, "no route configured", ct);
            return;
        }

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
                        await DispatchScpiAsync(stream, backend, device, scpi, ct);
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
                        await HandleAsyncLockAsync(stream, header, sessionId, ct);
                        break;
                    case HiSlipMessageType.AsyncReleaseLock:
                        ReleaseLock(sessionId);
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
        }
    }

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
            lock (_lockGate)
            {
                if (_lockHolder == 0 || _lockHolder == sessionId)
                {
                    _lockHolder = sessionId;
                    responseControl = 1; // granted
                }
                else
                {
                    responseControl = 0; // denied (someone else holds it)
                }
            }
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

    private static async Task DispatchScpiAsync(
        NetworkStream stream,
        IIviBackend backend,
        Domain.Devices.Device device,
        string scpi,
        CancellationToken ct
    )
    {
        if (scpi.TrimEnd('\r', '\n').EndsWith('?'))
        {
            var queryResult = ScpiQuery.From(scpi);
            if (queryResult is not Result<ScpiQuery, ScpiError>.Ok { Value: var q })
            {
                await SendFatalAsync(stream, "invalid SCPI query", ct);
                return;
            }
            var resp = await backend.QueryAsync(device, q, ct);
            if (resp is Result<string, BackendError>.Ok { Value: var responseText })
            {
                await SendDataEndAsync(stream, responseText, ct);
            }
            else
            {
                await SendFatalAsync(stream, "backend query failed", ct);
            }
        }
        else
        {
            var cmdResult = ScpiCommand.From(scpi);
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
        CancellationToken ct
    )
    {
        var bytes = Encoding.ASCII.GetBytes(responseText);
        var header = new byte[HiSlipMessage.HeaderSize];
        HiSlipMessage.WriteHeader(
            header,
            HiSlipMessageType.DataEnd,
            controlCode: 0,
            messageParameter: 0,
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
