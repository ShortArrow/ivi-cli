using System.Net;
using System.Net.Sockets;
using IviCli.Domain.Protocols;

namespace IviCli.Server.Tests;

/// <summary>Raw ONC RPC helpers for tests that talk to the VXI-11 gateway on the wire.</summary>
internal static class Vxi11Rpc
{
    /// <summary>An ONC RPC call record with AUTH_NONE credentials and <paramref name="body"/> as its arguments.</summary>
    internal static byte[] BuildRpcCall(
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

    /// <summary>A reader positioned after the header of an accepted RPC reply.</summary>
    internal static Vxi11XdrCodec.XdrReader SkipReplyHeader(byte[] reply)
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

    /// <summary>Waits until the gateway accepts connections on <paramref name="port"/>.</summary>
    internal static async Task WaitForListenerAsync(int port, CancellationToken ct)
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
