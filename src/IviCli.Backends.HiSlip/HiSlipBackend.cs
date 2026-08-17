using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Protocols;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;

namespace IviCli.Backends.HiSlip;

/// <summary>
/// Client-side <see cref="IIviBackend"/> for HiSLIP endpoints (PRD §7.2 /
/// ADR 0007). v1 supports the same subset as the gateway: synchronous
/// channel only, Data + DataEnd, fixed 16-byte header. The HiSLIP resource
/// is identified by a <see cref="VisaResource.Tcpip"/> whose <c>LanDevice</c>
/// starts with <c>hislip</c> (e.g. <c>hislip0</c>).
/// </summary>
public sealed class HiSlipBackend : IIviBackend
{
    private const ushort ProtocolVersion = 0x0100;

    /// <summary>The well-known HiSLIP TCP port per IVI-6.1 §10.</summary>
    public const int DefaultHiSlipPort = 4880;

    private readonly Dictionary<DeviceName, HiSlipSession> _sessions = new();
    private readonly object _gate = new();
    private readonly int _port;

    /// <summary>Creates a backend bound to the well-known HiSLIP port.</summary>
    public HiSlipBackend()
        : this(DefaultHiSlipPort) { }

    /// <summary>
    /// Creates a backend that connects to <paramref name="port"/> instead of
    /// the well-known HiSLIP port. Intended for tests against an in-process
    /// gateway listening on a randomly allocated loopback port.
    /// </summary>
    public HiSlipBackend(int port)
    {
        _port = port;
    }

    /// <inheritdoc/>
    public async Task<Result<Unit, BackendError>> OpenAsync(Device device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (device.Resource is not VisaResource.Tcpip tcpip)
        {
            return Result.Failure<Unit, BackendError>(
                new TransportDisconnected("HiSlipBackend only handles TCPIP::host::hislip*::INSTR")
            );
        }

        if (!tcpip.LanDevice.StartsWith("hislip", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<Unit, BackendError>(
                new TransportDisconnected(
                    $"HiSlipBackend expects LanDevice starting with 'hislip' (got '{tcpip.LanDevice}')"
                )
            );
        }

        // An explicit port in the resource (`hislip0,<port>`) overrides the
        // well-known port; otherwise use the backend default.
        var port = tcpip.Port ?? _port;

        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(tcpip.Host, port, ct);
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            client.Dispose();
            return Result.Failure<Unit, BackendError>(
                new TransportDisconnected($"connect failed: {ex.Message}", ex)
            );
        }

        var stream = client.GetStream();
        try
        {
            // Send Initialize with our protocol version and a minimal payload
            // (sub-protocol "" + LanDevice as client identifier).
            var payload = Encoding.ASCII.GetBytes(tcpip.LanDevice);
            var header = new byte[HiSlipMessage.HeaderSize];
            HiSlipMessage.WriteHeader(
                header,
                HiSlipMessageType.Initialize,
                controlCode: 0,
                messageParameter: ProtocolVersion,
                payloadLength: (ulong)payload.Length
            );
            await stream.WriteAsync(header, ct);
            if (payload.Length > 0)
            {
                await stream.WriteAsync(payload, ct);
            }

            // Expect InitializeResponse.
            await ReadExactlyAsync(stream, header, ct);
            var respHeader = HiSlipMessage.ReadHeader(header);
            if (respHeader.Type != HiSlipMessageType.InitializeResponse)
            {
                client.Dispose();
                return Result.Failure<Unit, BackendError>(
                    new TransportDisconnected($"unexpected HiSLIP response: {respHeader.Type}")
                );
            }
            if (respHeader.PayloadLength > 0)
            {
                var ignored = new byte[respHeader.PayloadLength];
                await ReadExactlyAsync(stream, ignored, ct);
            }

            // Session id is the low 16 bits of the InitializeResponse
            // message parameter (gateway encodes version << 16 | sessionId).
            var sessionId = (ushort)(respHeader.MessageParameter & 0xFFFF);

            // Open the async channel so we can receive ServiceRequest
            // notifications and (in a future batch) send AsyncDeviceClear /
            // AsyncLock without blocking the sync channel.
            var asyncChannel = await OpenAsyncChannelAsync(tcpip.Host, port, sessionId, ct);
            if (
                asyncChannel
                is not Result<(TcpClient client, NetworkStream stream), BackendError>.Ok asyncOk
            )
            {
                client.Dispose();
                return Result.Failure<Unit, BackendError>(
                    ((Result<(TcpClient, NetworkStream), BackendError>.Error)asyncChannel).Err
                );
            }
            var (asyncClient, asyncStream) = asyncOk.Value;
            var session = new HiSlipSession(client, asyncClient, asyncStream, device.Name);
            session.StartAsyncReader();

            lock (_gate)
            {
                if (_sessions.TryGetValue(device.Name, out var existing))
                {
                    existing.Dispose();
                }
                _sessions[device.Name] = session;
            }
            return Result.Success<Unit, BackendError>(Unit.Value);
        }
        catch (Exception ex) when (ex is SocketException or IOException or EndOfStreamException)
        {
            client.Dispose();
            return Result.Failure<Unit, BackendError>(
                new TransportDisconnected($"HiSLIP handshake failed: {ex.Message}", ex)
            );
        }
    }

    private static async Task<
        Result<(TcpClient, NetworkStream), BackendError>
    > OpenAsyncChannelAsync(string host, int port, ushort sessionId, CancellationToken ct)
    {
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host, port, ct);
            var stream = client.GetStream();
            var header = new byte[HiSlipMessage.HeaderSize];
            HiSlipMessage.WriteHeader(
                header,
                HiSlipMessageType.AsyncInitialize,
                controlCode: 0,
                messageParameter: sessionId,
                payloadLength: 0
            );
            await stream.WriteAsync(header, ct);
            await ReadExactlyAsync(stream, header, ct);
            var resp = HiSlipMessage.ReadHeader(header);
            if (resp.PayloadLength > 0)
            {
                var drain = new byte[resp.PayloadLength];
                await ReadExactlyAsync(stream, drain, ct);
            }
            if (resp.Type != HiSlipMessageType.AsyncInitializeResponse)
            {
                client.Dispose();
                return Result.Failure<(TcpClient, NetworkStream), BackendError>(
                    new TransportDisconnected($"unexpected async response: {resp.Type}")
                );
            }
            return Result.Success<(TcpClient, NetworkStream), BackendError>((client, stream));
        }
        catch (Exception ex) when (ex is SocketException or IOException or EndOfStreamException)
        {
            client.Dispose();
            return Result.Failure<(TcpClient, NetworkStream), BackendError>(
                new TransportDisconnected(
                    $"HiSLIP async-channel handshake failed: {ex.Message}",
                    ex
                )
            );
        }
    }

    /// <inheritdoc/>
    public Task<Result<Unit, BackendError>> CloseAsync(Device device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_sessions.Remove(device.Name, out var session))
            {
                session.Dispose();
            }
        }
        return Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));
    }

    /// <inheritdoc/>
    public async Task<Result<Unit, BackendError>> WriteAsync(
        Device device,
        ScpiCommand command,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var session = TryGetSession(device);
        if (session is null)
        {
            return Result.Failure<Unit, BackendError>(
                new TransportDisconnected("HiSLIP session not open")
            );
        }
        try
        {
            await SendDataEndAsync(session, command.Value, ct);
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            return Result.Failure<Unit, BackendError>(
                new TransportDisconnected($"write failed: {ex.Message}", ex)
            );
        }
        return Result.Success<Unit, BackendError>(Unit.Value);
    }

    /// <inheritdoc/>
    public async Task<Result<string, BackendError>> QueryAsync(
        Device device,
        ScpiQuery query,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var session = TryGetSession(device);
        if (session is null)
        {
            return Result.Failure<string, BackendError>(
                new TransportDisconnected("HiSLIP session not open")
            );
        }
        try
        {
            await SendDataEndAsync(session, query.Value, ct);
            return await ReceiveDataEndAsync(session, ct);
        }
        catch (Exception ex) when (ex is SocketException or IOException or EndOfStreamException)
        {
            return Result.Failure<string, BackendError>(
                new TransportDisconnected($"query failed: {ex.Message}", ex)
            );
        }
    }

    /// <inheritdoc/>
    public async Task<Result<string, BackendError>> ReadAsync(Device device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var session = TryGetSession(device);
        if (session is null)
        {
            return Result.Failure<string, BackendError>(
                new TransportDisconnected("HiSLIP session not open")
            );
        }
        try
        {
            return await ReceiveDataEndAsync(session, ct);
        }
        catch (Exception ex) when (ex is SocketException or IOException or EndOfStreamException)
        {
            return Result.Failure<string, BackendError>(
                new TransportDisconnected($"read failed: {ex.Message}", ex)
            );
        }
    }

    /// <inheritdoc/>
    public async Task<Result<Unit, BackendError>> TriggerAsync(Device device, CancellationToken ct)
    {
        var session = TryGetSession(device);
        if (session is null)
        {
            return Result.Failure<Unit, BackendError>(
                new TransportDisconnected("HiSLIP session not open")
            );
        }
        var header = new byte[HiSlipMessage.HeaderSize];
        HiSlipMessage.WriteHeader(
            header,
            HiSlipMessageType.Trigger,
            controlCode: 0,
            messageParameter: 0,
            payloadLength: 0
        );
        try
        {
            await session.Stream.WriteAsync(header, ct);
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            return Result.Failure<Unit, BackendError>(
                new TransportDisconnected($"HiSLIP Trigger write failed: {ex.Message}", ex)
            );
        }
        return Result.Success<Unit, BackendError>(Unit.Value);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ServiceRequest> ServiceRequestStream(
        Device device,
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        var session = TryGetSession(device);
        if (session is null)
        {
            yield break;
        }
        var reader = session.ServiceRequests.Reader;
        while (await reader.WaitToReadAsync(ct))
        {
            while (reader.TryRead(out var srq))
            {
                yield return srq;
            }
        }
    }

    private HiSlipSession? TryGetSession(Device device)
    {
        lock (_gate)
        {
            return _sessions.TryGetValue(device.Name, out var s) ? s : null;
        }
    }

    private static async Task SendDataEndAsync(
        HiSlipSession session,
        string text,
        CancellationToken ct
    )
    {
        var payload = Encoding.ASCII.GetBytes(text);
        var header = new byte[HiSlipMessage.HeaderSize];
        HiSlipMessage.WriteHeader(
            header,
            HiSlipMessageType.DataEnd,
            controlCode: 0,
            messageParameter: 0,
            payloadLength: (ulong)payload.Length
        );
        await session.Stream.WriteAsync(header, ct);
        if (payload.Length > 0)
        {
            await session.Stream.WriteAsync(payload, ct);
        }
    }

    private static async Task<Result<string, BackendError>> ReceiveDataEndAsync(
        HiSlipSession session,
        CancellationToken ct
    )
    {
        var headerBuf = new byte[HiSlipMessage.HeaderSize];
        var assembled = new StringBuilder();
        while (true)
        {
            await ReadExactlyAsync(session.Stream, headerBuf, ct);
            var header = HiSlipMessage.ReadHeader(headerBuf);
            if (header.Type == HiSlipMessageType.FatalError)
            {
                var reason = new byte[header.PayloadLength];
                if (reason.Length > 0)
                {
                    await ReadExactlyAsync(session.Stream, reason, ct);
                }
                return Result.Failure<string, BackendError>(
                    new TransportDisconnected($"server fatal: {Encoding.ASCII.GetString(reason)}")
                );
            }
            if (header.Type is not (HiSlipMessageType.Data or HiSlipMessageType.DataEnd))
            {
                return Result.Failure<string, BackendError>(
                    new TransportDisconnected($"unexpected HiSLIP message: {header.Type}")
                );
            }
            var payload = new byte[header.PayloadLength];
            if (payload.Length > 0)
            {
                await ReadExactlyAsync(session.Stream, payload, ct);
            }
            assembled.Append(Encoding.ASCII.GetString(payload));
            if (header.Type == HiSlipMessageType.DataEnd)
            {
                return Result.Success<string, BackendError>(assembled.ToString());
            }
        }
    }

    private static async Task ReadExactlyAsync(
        NetworkStream stream,
        byte[] buffer,
        CancellationToken ct
    )
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), ct);
            if (read <= 0)
            {
                throw new EndOfStreamException(
                    $"HiSLIP stream closed early at {offset}/{buffer.Length}"
                );
            }
            offset += read;
        }
    }

    private sealed class HiSlipSession : IDisposable
    {
        private readonly TcpClient _syncClient;
        private readonly TcpClient _asyncClient;
        private readonly NetworkStream _asyncStream;
        private readonly CancellationTokenSource _readerCts = new();
        private readonly DeviceName _deviceName;
        private Task? _readerTask;

        public HiSlipSession(
            TcpClient syncClient,
            TcpClient asyncClient,
            NetworkStream asyncStream,
            DeviceName deviceName
        )
        {
            _syncClient = syncClient;
            Stream = syncClient.GetStream();
            _asyncClient = asyncClient;
            _asyncStream = asyncStream;
            _deviceName = deviceName;
        }

        public NetworkStream Stream { get; }

        public Channel<ServiceRequest> ServiceRequests { get; } = ServiceRequestBuffer.Create();

        public void StartAsyncReader()
        {
            _readerTask = Task.Run(() => RunAsyncReaderAsync(_readerCts.Token));
        }

        private async Task RunAsyncReaderAsync(CancellationToken ct)
        {
            var header = new byte[HiSlipMessage.HeaderSize];
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await ReadExactlyAsync(_asyncStream, header, ct);
                    var parsed = HiSlipMessage.ReadHeader(header);
                    if (parsed.PayloadLength > 0)
                    {
                        var drain = new byte[parsed.PayloadLength];
                        await ReadExactlyAsync(_asyncStream, drain, ct);
                    }
                    if (parsed.Type == HiSlipMessageType.ServiceRequest)
                    {
                        // Per IVI-6.1 §10, the ServiceRequest control byte
                        // carries the STB value the server polled from the
                        // instrument.
                        ServiceRequests.Writer.TryWrite(
                            new ServiceRequest(
                                _deviceName,
                                parsed.ControlCode,
                                DateTimeOffset.UtcNow
                            )
                        );
                    }
                    // Other async-channel message types (AsyncLockResponse,
                    // AsyncDeviceClearAcknowledge, ...) get ignored — the
                    // client backend doesn't currently issue async requests.
                }
            }
            catch (OperationCanceledException)
            { /* graceful shutdown */
            }
            catch (Exception)
            { /* connection closed under us; nothing to do */
            }
            finally
            {
                ServiceRequests.Writer.TryComplete();
            }
        }

        public void Dispose()
        {
            try
            {
                _readerCts.Cancel();
            }
            catch
            { /* swallow */
            }
            try
            {
                Stream.Dispose();
            }
            catch
            { /* swallow */
            }
            try
            {
                _asyncStream.Dispose();
            }
            catch
            { /* swallow */
            }
            _syncClient.Dispose();
            _asyncClient.Dispose();
            try
            {
                _readerTask?.Wait(TimeSpan.FromMilliseconds(200));
            }
            catch
            { /* swallow */
            }
            _readerCts.Dispose();
        }
    }
}
