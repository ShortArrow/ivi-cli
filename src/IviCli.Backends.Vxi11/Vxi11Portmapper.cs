using System.Net.Sockets;
using IviCli.Domain.Protocols;
using static IviCli.Domain.Protocols.Vxi11Constants;

namespace IviCli.Backends.Vxi11;

/// <summary>
/// ONC RPC portmapper (RFC 1833) GETPORT helpers shared by the VXI-11
/// client backend and the broadcast scanner. The request/reply wire
/// format is transport-agnostic: the scanner sends the raw bytes over
/// UDP broadcast, while the client wraps them in TCP record-marking
/// framing for a unicast round-trip against an instrument's portmapper
/// at <see cref="Vxi11Constants.PortmapperPort"/>.
/// </summary>
public static class Vxi11Portmapper
{
    /// <summary>
    /// Connects to <paramref name="host"/>:<paramref name="portmapperPort"/>
    /// over TCP, issues <c>PMAPPROC_GETPORT</c> for the VXI-11 Device Core
    /// program, and returns the TCP port the Core listens on (0 if the
    /// program is not registered). Throws <see cref="SocketException"/> /
    /// <see cref="IOException"/> when the portmapper is unreachable so the
    /// caller can fall back to a fixed port.
    /// </summary>
    public static async Task<int> ResolveCorePortAsync(
        string host,
        int portmapperPort,
        TimeSpan timeout,
        CancellationToken ct
    )
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        using var client = new TcpClient();
        await client.ConnectAsync(host, portmapperPort, cts.Token);
        var stream = client.GetStream();

        var xid = unchecked((uint)Random.Shared.Next(int.MinValue, int.MaxValue));
        await Vxi11RecordFraming.WriteRecordAsync(stream, BuildGetportRequest(xid), cts.Token);
        var reply = await Vxi11RecordFraming.ReadRecordAsync(stream, cts.Token);
        return TryParseGetportReply(reply, xid, out var port) ? port : 0;
    }

    /// <summary>
    /// Builds the raw ONC RPC <c>PMAPPROC_GETPORT</c> request asking for the
    /// VXI-11 Device Core program over TCP. The bytes carry no record-marking
    /// header, so UDP callers send them verbatim and TCP callers wrap them
    /// via <see cref="Vxi11RecordFraming.WriteRecordAsync"/>.
    /// </summary>
    public static byte[] BuildGetportRequest(uint xid)
    {
        var w = new Vxi11XdrCodec.XdrWriter();
        w.WriteUInt32(xid);
        w.WriteUInt32(0); // mtype = CALL
        w.WriteUInt32(2); // rpcvers
        w.WriteUInt32(PortmapProgram);
        w.WriteUInt32(PortmapVersion);
        w.WriteUInt32(PortmapGetPort);
        w.WriteUInt32(0); // cred flavor (AUTH_NONE)
        w.WriteUInt32(0); // cred length
        w.WriteUInt32(0); // verf flavor (AUTH_NONE)
        w.WriteUInt32(0); // verf length

        // pmap mapping struct: { prog, vers, prot, port }.
        w.WriteUInt32(CoreProgram);
        w.WriteUInt32(CoreVersion);
        w.WriteUInt32(IpProtoTcp);
        w.WriteUInt32(0); // port is ignored on GETPORT
        return w.ToArray();
    }

    /// <summary>
    /// Parses a portmapper GETPORT reply, validating the RPC envelope and
    /// extracting the returned port. Returns <c>false</c> when the reply is
    /// malformed, rejected, or for a different transaction id.
    /// </summary>
    public static bool TryParseGetportReply(
        ReadOnlySpan<byte> buffer,
        uint expectedXid,
        out int port
    )
    {
        port = 0;
        // xid + mtype + reply_stat + verf(flavor+len) + accept_stat + port = 7 words.
        if (buffer.Length < 28)
        {
            return false;
        }
        try
        {
            var reader = new Vxi11XdrCodec.XdrReader(buffer.ToArray());
            if (reader.ReadUInt32() != expectedXid)
            {
                return false;
            }
            if (reader.ReadUInt32() != 1) // mtype = REPLY
            {
                return false;
            }
            if (reader.ReadUInt32() != MsgAccepted)
            {
                return false;
            }
            _ = reader.ReadUInt32(); // verf flavor
            _ = reader.ReadOpaque(); // verf body (length-prefixed)
            if (reader.ReadUInt32() != AcceptSuccess)
            {
                return false;
            }
            if (reader.Remaining < 4)
            {
                return false;
            }
            port = (int)reader.ReadUInt32();
            return true;
        }
        catch (InvalidDataException)
        {
            // Malformed / truncated datagram (UDP noise) — not a valid reply.
            return false;
        }
    }
}
