using System.Net.Sockets;
using System.Text;
using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Protocols;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;
using static IviCli.Domain.Protocols.Vxi11Constants;

namespace IviCli.Backends.Vxi11;

/// <summary>
/// Client-side <see cref="IIviBackend"/> for VXI-11 endpoints
/// (PRD §7.1 priority 2, ADR 0029). v1 implements create_link /
/// device_write / device_read / destroy_link over a single TCP
/// connection per device. The portmapper round-trip is deliberately
/// skipped — ivi-cli's gateway co-locates portmapper + Core on the
/// same bind port, so a real GETPORT call would only echo back the
/// port we already connected to. Real-portmapper-at-111 support is
/// deferred to v2.
/// </summary>
public sealed class Vxi11Backend : IIviBackend
{
    /// <summary>
    /// Fallback TCP port when the constructor override is not used.
    /// VXI-11 has no IANA-assigned core port (clients traditionally
    /// learn it from portmapper at 111); this value is a placeholder
    /// suitable for ad-hoc deployments where the gateway operator
    /// configures the server with a matching port.
    /// </summary>
    public const int DefaultVxi11Port = 1024;

    private readonly Dictionary<DeviceName, Vxi11Session> _sessions = new();
    private readonly object _gate = new();
    private readonly int _port;

    /// <summary>Creates a backend bound to <see cref="DefaultVxi11Port"/>.</summary>
    public Vxi11Backend()
        : this(DefaultVxi11Port) { }

    /// <summary>
    /// Creates a backend that connects to <paramref name="port"/> instead of
    /// the default. Intended for tests against an in-process gateway listening
    /// on a randomly allocated loopback port.
    /// </summary>
    public Vxi11Backend(int port)
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
                new TransportDisconnected("Vxi11Backend only handles TCPIP::host::inst*::INSTR")
            );
        }
        if (!tcpip.LanDevice.StartsWith("inst", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<Unit, BackendError>(
                new TransportDisconnected(
                    $"Vxi11Backend expects LanDevice starting with 'inst' (got '{tcpip.LanDevice}')"
                )
            );
        }

        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(tcpip.Host, _port, ct);
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            client.Dispose();
            return Result.Failure<Unit, BackendError>(
                new TransportDisconnected($"connect failed: {ex.Message}", ex)
            );
        }

        var session = new Vxi11Session(client);
        try
        {
            var lid = await CreateLinkAsync(session, tcpip.LanDevice, ct);
            session.LinkId = lid;
        }
        catch (Exception ex) when (ex is SocketException or IOException or InvalidDataException)
        {
            session.Dispose();
            return Result.Failure<Unit, BackendError>(
                new TransportDisconnected($"VXI-11 create_link failed: {ex.Message}", ex)
            );
        }

        lock (_gate)
        {
            if (_sessions.Remove(device.Name, out var prior))
            {
                prior.Dispose();
            }
            _sessions[device.Name] = session;
        }
        return Result.Success<Unit, BackendError>(Unit.Value);
    }

    /// <inheritdoc/>
    public async Task<Result<Unit, BackendError>> CloseAsync(Device device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Vxi11Session? session;
        lock (_gate)
        {
            _sessions.Remove(device.Name, out session);
        }
        if (session is null)
        {
            return Result.Success<Unit, BackendError>(Unit.Value);
        }
        try
        {
            await DestroyLinkAsync(session, ct);
        }
        catch (Exception ex) when (ex is SocketException or IOException or InvalidDataException)
        {
            // The session is going away regardless; surface the cleanup
            // failure once and continue tearing down the TCP client.
            session.Dispose();
            return Result.Failure<Unit, BackendError>(
                new TransportDisconnected($"VXI-11 destroy_link failed: {ex.Message}", ex)
            );
        }
        session.Dispose();
        return Result.Success<Unit, BackendError>(Unit.Value);
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
                new TransportDisconnected("VXI-11 session not open")
            );
        }
        try
        {
            await DeviceWriteAsync(session, command.Value, ct);
            return Result.Success<Unit, BackendError>(Unit.Value);
        }
        catch (Exception ex) when (ex is SocketException or IOException or InvalidDataException)
        {
            return Result.Failure<Unit, BackendError>(
                new TransportDisconnected($"VXI-11 device_write failed: {ex.Message}", ex)
            );
        }
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
                new TransportDisconnected("VXI-11 session not open")
            );
        }
        try
        {
            await DeviceWriteAsync(session, query.Value, ct);
            var text = await DeviceReadAsync(session, ct);
            return Result.Success<string, BackendError>(text);
        }
        catch (Exception ex) when (ex is SocketException or IOException or InvalidDataException)
        {
            return Result.Failure<string, BackendError>(
                new TransportDisconnected($"VXI-11 query failed: {ex.Message}", ex)
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
                new TransportDisconnected("VXI-11 session not open")
            );
        }
        try
        {
            var text = await DeviceReadAsync(session, ct);
            return Result.Success<string, BackendError>(text);
        }
        catch (Exception ex) when (ex is SocketException or IOException or InvalidDataException)
        {
            return Result.Failure<string, BackendError>(
                new TransportDisconnected($"VXI-11 device_read failed: {ex.Message}", ex)
            );
        }
    }

    /// <inheritdoc/>
    public Task<Result<Unit, BackendError>> TriggerAsync(Device device, CancellationToken ct)
    {
        // VXI-11 device_trigger (proc 17) wiring lands in Batch P Task 3.
        return Task.FromResult(
            Result.Failure<Unit, BackendError>(
                new BackendOperationNotSupported(
                    "trigger",
                    device.Name,
                    "Vxi11Backend device_trigger lands in Batch P Task 3"
                )
            )
        );
    }

    /// <inheritdoc/>
#pragma warning disable CS1998
    public async IAsyncEnumerable<ServiceRequest> ServiceRequestStream(
        Device device,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct
    )
    {
        // VXI-11 Interrupt channel (program 395185) is explicitly deferred
        // to v2 — see ADR 0041 §5 / ADR 0029 §2. v1 stream completes
        // immediately.
        yield break;
    }
#pragma warning restore CS1998

    private Vxi11Session? TryGetSession(Device device)
    {
        lock (_gate)
        {
            return _sessions.TryGetValue(device.Name, out var s) ? s : null;
        }
    }

    private static async Task<int> CreateLinkAsync(
        Vxi11Session session,
        string lanDevice,
        CancellationToken ct
    )
    {
        var call = BuildCall(
            session.NextXid(),
            CoreProgram,
            CoreVersion,
            ProcCreateLink,
            body =>
            {
                body.WriteInt32(Environment.ProcessId);
                body.WriteUInt32(0); // lockDevice = false
                body.WriteUInt32(0); // lock_timeout
                body.WriteString(lanDevice);
            }
        );
        await Vxi11RecordFraming.WriteRecordAsync(session.Stream, call, ct);
        var reply = SkipReplyHeader(await Vxi11RecordFraming.ReadRecordAsync(session.Stream, ct));
        var error = reply.ReadInt32();
        if (error != Vxi11NoError)
        {
            throw new InvalidDataException($"create_link returned error {error}");
        }
        var lid = reply.ReadInt32();
        _ = reply.ReadUInt32(); // abortPort
        _ = reply.ReadUInt32(); // maxRecvSize
        return lid;
    }

    private static async Task DestroyLinkAsync(Vxi11Session session, CancellationToken ct)
    {
        var call = BuildCall(
            session.NextXid(),
            CoreProgram,
            CoreVersion,
            ProcDestroyLink,
            body => body.WriteInt32(session.LinkId)
        );
        await Vxi11RecordFraming.WriteRecordAsync(session.Stream, call, ct);
        var reply = SkipReplyHeader(await Vxi11RecordFraming.ReadRecordAsync(session.Stream, ct));
        var error = reply.ReadInt32();
        if (error != Vxi11NoError)
        {
            throw new InvalidDataException($"destroy_link returned error {error}");
        }
    }

    private static async Task DeviceWriteAsync(
        Vxi11Session session,
        string scpi,
        CancellationToken ct
    )
    {
        var data = Encoding.ASCII.GetBytes(scpi);
        var call = BuildCall(
            session.NextXid(),
            CoreProgram,
            CoreVersion,
            ProcDeviceWrite,
            body =>
            {
                body.WriteInt32(session.LinkId);
                body.WriteUInt32(5000); // io_timeout
                body.WriteUInt32(0); // lock_timeout
                body.WriteInt32(WriteEndFlag);
                body.WriteOpaque(data);
            }
        );
        await Vxi11RecordFraming.WriteRecordAsync(session.Stream, call, ct);
        var reply = SkipReplyHeader(await Vxi11RecordFraming.ReadRecordAsync(session.Stream, ct));
        var error = reply.ReadInt32();
        if (error != Vxi11NoError)
        {
            throw new InvalidDataException($"device_write returned error {error}");
        }
        _ = reply.ReadUInt32(); // size acknowledged by server
    }

    private static async Task<string> DeviceReadAsync(Vxi11Session session, CancellationToken ct)
    {
        var assembled = new StringBuilder();
        for (var fragment = 0; fragment < 64; fragment++)
        {
            var call = BuildCall(
                session.NextXid(),
                CoreProgram,
                CoreVersion,
                ProcDeviceRead,
                body =>
                {
                    body.WriteInt32(session.LinkId);
                    body.WriteUInt32(4096); // requestSize
                    body.WriteUInt32(5000); // io_timeout
                    body.WriteUInt32(0); // lock_timeout
                    body.WriteInt32(0); // flags
                    body.WriteUInt32((byte)'\n'); // termChar
                }
            );
            await Vxi11RecordFraming.WriteRecordAsync(session.Stream, call, ct);
            var reply = SkipReplyHeader(
                await Vxi11RecordFraming.ReadRecordAsync(session.Stream, ct)
            );
            var error = reply.ReadInt32();
            if (error != Vxi11NoError)
            {
                throw new InvalidDataException($"device_read returned error {error}");
            }
            var reason = reply.ReadInt32();
            var data = reply.ReadOpaque();
            assembled.Append(Encoding.ASCII.GetString(data));
            if ((reason & ReadReasonEnd) != 0)
            {
                break;
            }
        }
        return assembled.ToString().TrimEnd('\r', '\n');
    }

    private static byte[] BuildCall(
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

    private static Vxi11XdrCodec.XdrReader SkipReplyHeader(byte[] reply)
    {
        var reader = new Vxi11XdrCodec.XdrReader(reply);
        _ = reader.ReadUInt32(); // xid
        _ = reader.ReadUInt32(); // mtype = REPLY
        var replyStat = reader.ReadUInt32();
        if (replyStat != MsgAccepted)
        {
            throw new InvalidDataException($"RPC reply was rejected (reply_stat={replyStat})");
        }
        _ = reader.ReadUInt32(); // verf flavor
        _ = reader.ReadOpaque(); // verf body
        var acceptStat = reader.ReadUInt32();
        if (acceptStat != AcceptSuccess)
        {
            throw new InvalidDataException(
                $"RPC reply accept_stat={acceptStat} (program / proc unavailable)"
            );
        }
        return reader;
    }
}
