using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using IviCli.Application.Backends;
using IviCli.Domain.Devices;
using IviCli.Domain.Protocols;

namespace IviCli.Backends.Vxi11;

/// <summary>
/// Per-Device session state owned by <see cref="Vxi11Backend"/>.
/// Wraps the TCP client + stream, tracks the link id assigned by
/// <c>create_link</c> plus a monotonic XID counter, and owns the
/// Interrupt-channel listener that decodes inbound device_intr_srq
/// (ADR 0042).
/// </summary>
internal sealed class Vxi11Session : IDisposable
{
    private readonly TcpClient _client;
    private readonly DeviceName _deviceName;
    private readonly CancellationTokenSource _interruptCts = new();
    private uint _nextXid;
    private TcpListener? _interruptListener;
    private Task? _interruptAcceptTask;

    public Vxi11Session(TcpClient client, DeviceName deviceName)
    {
        _client = client;
        _deviceName = deviceName;
        Stream = client.GetStream();
    }

    public NetworkStream Stream { get; }

    /// <summary>Set by <see cref="Vxi11Backend"/> after <c>create_link</c>.</summary>
    public int LinkId { get; set; }

    /// <summary>Returns the next RPC transaction id (starts at 1).</summary>
    public uint NextXid() => System.Threading.Interlocked.Increment(ref _nextXid);

    /// <summary>Inbound SRQ events delivered by the Interrupt channel listener.</summary>
    public Channel<ServiceRequest> ServiceRequests { get; } =
        Channel.CreateUnbounded<ServiceRequest>();

    /// <summary>The TCP host the gateway must connect to for SRQs (loopback v4).</summary>
    public uint InterruptHostAddr { get; private set; }

    /// <summary>The TCP port the gateway must connect to for SRQs.</summary>
    public uint InterruptPort { get; private set; }

    /// <summary>Opaque handle bytes the gateway echoes back on each SRQ.</summary>
    public byte[] InterruptHandle { get; } = NewHandle();

    /// <summary>True when the SRQ setup RPCs failed; ServiceRequestStream completes empty.</summary>
    public bool InterruptSetupFailed { get; private set; }

    /// <summary>Starts the loopback TCP listener that accepts inbound device_intr_srq calls.</summary>
    public void StartInterruptListener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var bytes = endpoint.Address.MapToIPv4().GetAddressBytes();
        InterruptHostAddr =
            (uint)bytes[0] << 24 | (uint)bytes[1] << 16 | (uint)bytes[2] << 8 | bytes[3];
        InterruptPort = (uint)endpoint.Port;
        _interruptListener = listener;
        _interruptAcceptTask = Task.Run(() => RunAcceptLoopAsync(_interruptCts.Token));
    }

    public void MarkInterruptSetupFailed(Exception ex)
    {
        InterruptSetupFailed = true;
        ServiceRequests.Writer.TryComplete(ex);
    }

    private async Task RunAcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _interruptListener is not null)
            {
                TcpClient inbound;
                try
                {
                    inbound = await _interruptListener.AcceptTcpClientAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                _ = Task.Run(() => HandleInboundAsync(inbound, ct), ct);
            }
        }
        finally
        {
            ServiceRequests.Writer.TryComplete();
        }
    }

    private async Task HandleInboundAsync(TcpClient inbound, CancellationToken ct)
    {
        try
        {
            using var tcp = inbound;
            using var stream = tcp.GetStream();
            // The gateway's RunSrqForwarder may send multiple device_intr_srq
            // calls on the same TCP connection — read in a loop.
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
                _ = reader.ReadUInt32(); // xid
                _ = reader.ReadUInt32(); // mtype = CALL
                _ = reader.ReadUInt32(); // rpcvers
                _ = reader.ReadUInt32(); // prog
                _ = reader.ReadUInt32(); // vers
                _ = reader.ReadUInt32(); // proc
                _ = reader.ReadUInt32(); // cred flavor
                _ = reader.ReadOpaque(); // cred body
                _ = reader.ReadUInt32(); // verf flavor
                _ = reader.ReadOpaque(); // verf body
                var srqParms = Vxi11InterruptCodec.ReadSrqParms(ref reader);
                ServiceRequests.Writer.TryWrite(
                    new ServiceRequest(
                        _deviceName,
                        srqParms.Handle.Length > 0 ? srqParms.Handle[0] : (byte)0,
                        DateTimeOffset.UtcNow
                    )
                );
                // Reply with an empty success body so the server can drain.
                var reply = new Vxi11XdrCodec.XdrWriter();
                reply.WriteUInt32(0); // xid (best-effort)
                reply.WriteUInt32(1); // mtype = REPLY
                reply.WriteUInt32(0); // MSG_ACCEPTED
                reply.WriteUInt32(0); // verf flavor
                reply.WriteUInt32(0); // verf length
                reply.WriteUInt32(0); // accept status SUCCESS
                await Vxi11RecordFraming.WriteRecordAsync(stream, reply.ToArray(), ct);
            }
        }
        catch (Exception)
        { /* graceful client disconnect */
        }
    }

    public void Dispose()
    {
        try
        {
            _interruptCts.Cancel();
        }
        catch
        { /* swallow */
        }
        try
        {
            _interruptListener?.Stop();
        }
        catch
        { /* swallow */
        }
        ServiceRequests.Writer.TryComplete();
        try
        {
            _interruptAcceptTask?.Wait(TimeSpan.FromMilliseconds(200));
        }
        catch
        { /* swallow */
        }
        _interruptCts.Dispose();
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

    private static byte[] NewHandle()
    {
        var handle = new byte[4];
        Random.Shared.NextBytes(handle);
        return handle;
    }
}
