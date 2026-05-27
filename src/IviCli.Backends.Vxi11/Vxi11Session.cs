using System.Net.Sockets;

namespace IviCli.Backends.Vxi11;

/// <summary>
/// Per-Device session state owned by <see cref="Vxi11Backend"/>.
/// Wraps the TCP client + stream and tracks the link id assigned by
/// <c>create_link</c> plus a monotonic XID counter so RPC calls land
/// with distinct transaction ids.
/// </summary>
internal sealed class Vxi11Session : IDisposable
{
    private readonly TcpClient _client;
    private uint _nextXid;

    public Vxi11Session(TcpClient client)
    {
        _client = client;
        Stream = client.GetStream();
    }

    public NetworkStream Stream { get; }

    /// <summary>Set by <see cref="Vxi11Backend"/> after <c>create_link</c>.</summary>
    public int LinkId { get; set; }

    /// <summary>Returns the next RPC transaction id (starts at 1).</summary>
    public uint NextXid() => System.Threading.Interlocked.Increment(ref _nextXid);

    public void Dispose()
    {
        try
        {
            Stream.Dispose();
        }
        catch
        {
            // best-effort; the TCP client below will tear down regardless.
        }
        _client.Dispose();
    }
}
