using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Visa;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IviCli.Backends.Vxi11;

/// <summary>
/// VXI-11 portmapper broadcast scanner (ADR 0008, Batch W).
///
/// Sends an ONC RPC <c>PMAPPROC_GETPORT</c> request asking for the
/// VXI-11 Device Core program (0x0607AF) over UDP broadcast
/// (255.255.255.255:111). Any host with a VXI-11 server registered
/// answers with the TCP port it listens on; the scanner builds a
/// <c>TCPIP::host::inst0::INSTR</c> resource for each responder.
///
/// Discovery window is bounded by a configurable timeout (default
/// 3 s). The scanner intentionally does not chase the per-host TCP
/// port (e.g. by issuing a follow-up <c>create_link</c>) — the
/// presence of a portmapper registration is sufficient evidence
/// that <c>ivicli visa add</c> + the standard backend dispatch will
/// reach the instrument.
/// </summary>
public sealed class Vxi11BroadcastScanner : IBackendScanner
{
    // Portmapper (RFC 1833) constants.
    private const uint PortmapProgram = 100000;
    private const uint PortmapVersion = 2;
    private const uint PmapprocGetport = 3;
    private const int PortmapPort = 111;

    // VXI-11 Device Core program identifier (per IVI VXI-11 §B).
    private const uint Vxi11DeviceCoreProgram = 0x0607AF;
    private const uint Vxi11DeviceCoreVersion = 1;
    private const uint IpprotoTcp = 6;

    // RPC framing constants.
    private const uint RpcCall = 0;
    private const uint RpcReply = 1;
    private const uint RpcVersion = 2;
    private const uint AuthNone = 0;
    private const uint MsgAccepted = 0;
    private const uint SuccessState = 0;

    private readonly TimeSpan _discoveryWindow;
    private readonly ILogger<Vxi11BroadcastScanner> _logger;

    /// <summary>Creates a scanner that listens for portmapper replies for the supplied window (default 3 s).</summary>
    public Vxi11BroadcastScanner(
        TimeSpan? discoveryWindow = null,
        ILogger<Vxi11BroadcastScanner>? logger = null
    )
    {
        _discoveryWindow = discoveryWindow ?? TimeSpan.FromSeconds(3);
        _logger = logger ?? NullLogger<Vxi11BroadcastScanner>.Instance;
    }

    /// <inheritdoc/>
    public async Task<Result<ImmutableArray<DiscoveredResource>, BackendError>> ScanAsync(
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();

        var responders = new ConcurrentDictionary<IPAddress, ushort>();

        using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };

        // Bind to an ephemeral port so replies have somewhere to land.
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        var xid = (uint)Random.Shared.Next(int.MinValue, int.MaxValue);
        var request = BuildGetportRequest(xid);

        try
        {
            await udp.SendAsync(request, new IPEndPoint(IPAddress.Broadcast, PortmapPort), ct)
                .ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            _logger.LogDebug(ex, "VXI-11 broadcast send failed");
            return Result.Success<ImmutableArray<DiscoveredResource>, BackendError>(
                ImmutableArray<DiscoveredResource>.Empty
            );
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_discoveryWindow);

        try
        {
            while (true)
            {
                cts.Token.ThrowIfCancellationRequested();
                UdpReceiveResult datagram;
                try
                {
                    datagram = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    _logger.LogDebug(ex, "VXI-11 broadcast receive failed");
                    break;
                }

                if (TryParseGetportReply(datagram.Buffer, xid, out var port) && port > 0)
                {
                    responders[datagram.RemoteEndPoint.Address] = port;
                }
            }
        }
        finally
        {
            // UdpClient.Dispose handles socket teardown.
        }

        var resources = responders
            .Select(kvp => BuildDiscovered(kvp.Key))
            .Where(r => r is not null)
            .Select(r => r!)
            .ToImmutableArray();

        return Result.Success<ImmutableArray<DiscoveredResource>, BackendError>(resources);
    }

    private static byte[] BuildGetportRequest(uint xid)
    {
        // RPC CALL header (10 uint32 words = 40 bytes) + AUTH_NONE cred +
        // AUTH_NONE verf (2 × 8 bytes = 16 bytes) + GETPORT mapping
        // (4 × 4 bytes = 16 bytes) = 72 bytes total.
        Span<byte> buffer = stackalloc byte[72];
        var pos = 0;

        WriteUInt32(buffer, ref pos, xid);
        WriteUInt32(buffer, ref pos, RpcCall);
        WriteUInt32(buffer, ref pos, RpcVersion);
        WriteUInt32(buffer, ref pos, PortmapProgram);
        WriteUInt32(buffer, ref pos, PortmapVersion);
        WriteUInt32(buffer, ref pos, PmapprocGetport);

        // AUTH_NONE credentials: flavor=0, length=0.
        WriteUInt32(buffer, ref pos, AuthNone);
        WriteUInt32(buffer, ref pos, 0);
        // AUTH_NONE verifier: flavor=0, length=0.
        WriteUInt32(buffer, ref pos, AuthNone);
        WriteUInt32(buffer, ref pos, 0);

        // Mapping struct.
        WriteUInt32(buffer, ref pos, Vxi11DeviceCoreProgram);
        WriteUInt32(buffer, ref pos, Vxi11DeviceCoreVersion);
        WriteUInt32(buffer, ref pos, IpprotoTcp);
        WriteUInt32(buffer, ref pos, 0); // port is ignored on GETPORT

        return buffer.ToArray();
    }

    private static bool TryParseGetportReply(
        ReadOnlySpan<byte> buffer,
        uint expectedXid,
        out ushort port
    )
    {
        port = 0;
        // Minimal successful reply: xid + msg_type + reply_state +
        // verf(flavor+length) + accept_state + port = 7 × 4 = 28 bytes.
        if (buffer.Length < 28)
        {
            return false;
        }
        var pos = 0;
        var xid = ReadUInt32(buffer, ref pos);
        if (xid != expectedXid)
        {
            return false;
        }
        var msgType = ReadUInt32(buffer, ref pos);
        if (msgType != RpcReply)
        {
            return false;
        }
        var replyState = ReadUInt32(buffer, ref pos);
        if (replyState != MsgAccepted)
        {
            return false;
        }
        // Verifier flavor + length (length must be 0 for AUTH_NONE replies).
        var verfFlavor = ReadUInt32(buffer, ref pos);
        var verfLen = ReadUInt32(buffer, ref pos);
        if (verfLen != 0)
        {
            // Skip the verifier opaque body if present.
            pos += (int)verfLen;
            if (pos > buffer.Length)
            {
                return false;
            }
        }
        _ = verfFlavor;
        var acceptState = ReadUInt32(buffer, ref pos);
        if (acceptState != SuccessState)
        {
            return false;
        }
        if (pos + 4 > buffer.Length)
        {
            return false;
        }
        var rawPort = ReadUInt32(buffer, ref pos);
        port = (ushort)rawPort;
        return true;
    }

    private static void WriteUInt32(Span<byte> buffer, ref int pos, uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(pos, 4), value);
        pos += 4;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> buffer, ref int pos)
    {
        var value = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(pos, 4));
        pos += 4;
        return value;
    }

    private static DiscoveredResource? BuildDiscovered(IPAddress host)
    {
        var raw = $"TCPIP0::{host}::inst0::INSTR";
        var parsed = VisaResource.Parse(raw);
        if (parsed is not Result<VisaResource, VisaResourceError>.Ok { Value: var resource })
        {
            return null;
        }
        return new DiscoveredResource(resource, Idn: null);
    }
}
