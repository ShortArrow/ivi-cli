using System.Net.Sockets;

namespace IviCli.Server;

/// <summary>
/// Tells a peer that reset its connection apart from a gateway fault, so a
/// connection handler can log the former in one line and keep the stack
/// trace for the latter.
/// </summary>
internal static class PeerAbort
{
    /// <summary>
    /// Whether <paramref name="ex"/> is the peer resetting the connection
    /// (killed client, abortive close) rather than a gateway fault. A write
    /// after the reset fails with <see cref="SocketError.Shutdown"/> (EPIPE)
    /// on Linux; the gateway never shuts its own sockets down, so that too
    /// means the peer is gone.
    /// </summary>
    public static bool Is(IOException ex, out SocketError socketError)
    {
        socketError = ex.InnerException is SocketException se
            ? se.SocketErrorCode
            : SocketError.Success;
        return socketError
            is SocketError.ConnectionReset
                or SocketError.ConnectionAborted
                or SocketError.Shutdown;
    }
}
