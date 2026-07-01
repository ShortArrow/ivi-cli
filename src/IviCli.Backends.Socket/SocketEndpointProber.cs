using System.Net.Sockets;
using System.Text;
using IviCli.Application.Backends;

namespace IviCli.Backends.Socket;

/// <summary>
/// Real TCP implementation of <see cref="IEndpointProber"/> for the
/// <c>visa scan</c> sweep and enrichment passes (ADR 0008): a
/// bounded-timeout connect, optionally followed by a <c>*IDN?</c> exchange
/// to confirm the port speaks SCPI and capture the instrument model.
/// </summary>
public sealed class SocketEndpointProber : IEndpointProber
{
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _readTimeout;

    /// <summary>
    /// Creates a prober with the given connect / read timeouts (defaults 400 ms
    /// and 800 ms — short enough to keep a /24 sweep responsive).
    /// </summary>
    public SocketEndpointProber(TimeSpan? connectTimeout = null, TimeSpan? readTimeout = null)
    {
        _connectTimeout = connectTimeout ?? TimeSpan.FromMilliseconds(400);
        _readTimeout = readTimeout ?? TimeSpan.FromMilliseconds(800);
    }

    /// <inheritdoc/>
    public async Task<EndpointProbe> ProbeAsync(
        string host,
        int port,
        bool identify,
        CancellationToken ct
    )
    {
        using var client = new TcpClient();
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(_connectTimeout);
        try
        {
            await client.ConnectAsync(host, port, connectCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
            when (ex is SocketException
                || (ex is OperationCanceledException && !ct.IsCancellationRequested)
            )
        {
            // Refused, filtered, or connect timed out — the port is not open.
            return new EndpointProbe(Open: false, Idn: null);
        }

        if (!identify)
        {
            return new EndpointProbe(Open: true, Idn: null);
        }

        return new EndpointProbe(Open: true, Idn: await TryIdentifyAsync(client, ct));
    }

    private async Task<string?> TryIdentifyAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCts.CancelAfter(_readTimeout);
            var stream = client.GetStream();
            await stream
                .WriteAsync(Encoding.ASCII.GetBytes("*IDN?\n"), readCts.Token)
                .ConfigureAwait(false);
            var buffer = new byte[512];
            var read = await stream.ReadAsync(buffer, readCts.Token).ConfigureAwait(false);
            if (read <= 0)
            {
                return null;
            }
            var text = Encoding.ASCII.GetString(buffer, 0, read).Trim('\r', '\n', ' ', '\0');
            return text.Length == 0 ? null : text;
        }
        catch (Exception ex)
            when (ex is SocketException or IOException
                || (ex is OperationCanceledException && !ct.IsCancellationRequested)
            )
        {
            // Open but not SCPI-identifiable (no response / not a SCPI-RAW port).
            return null;
        }
    }
}
