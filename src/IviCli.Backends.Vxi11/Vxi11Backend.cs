using System.Buffers.Binary;
using System.Net;
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
using static IviCli.Domain.Protocols.Vxi11Constants;

namespace IviCli.Backends.Vxi11;

/// <summary>
/// Client-side <see cref="IIviBackend"/> for VXI-11 endpoints
/// (PRD §7.1 priority 2, ADR 0029). Implements create_link /
/// device_write / device_read / destroy_link over a single TCP
/// connection per device.
///
/// On open the client first asks the instrument's portmapper at UDP/111
/// for the dynamically-assigned VXI-11 Core port (a real GETPORT
/// round-trip — issue #20). When no portmapper answers (e.g. ivi-cli's
/// own gateway, which co-locates portmapper + Core on a single bind
/// port and does not answer GETPORT on 111) it falls back to the fixed
/// port, preserving the gateway pairing.
/// </summary>
public sealed class Vxi11Backend : IIviBackend
{
    /// <summary>
    /// Fallback TCP port used when no portmapper answers. VXI-11 has no
    /// IANA-assigned core port; this value matches the port ivi-cli's
    /// gateway binds by default for ad-hoc deployments.
    /// </summary>
    public const int DefaultVxi11Port = 1024;

    /// <summary>How long to wait for the portmapper round-trip before falling back.</summary>
    private static readonly TimeSpan PortmapperProbeTimeout = TimeSpan.FromSeconds(3);

    private readonly Dictionary<DeviceName, Vxi11Session> _sessions = new();
    private readonly object _gate = new();
    private readonly int _fallbackPort;
    private readonly int _portmapperPort;
    private readonly bool _usePortmapper;

    /// <summary>
    /// Production constructor: resolves the Core port via the portmapper at
    /// <see cref="Vxi11Constants.PortmapperPort"/>, falling back to
    /// <see cref="DefaultVxi11Port"/>.
    /// </summary>
    public Vxi11Backend()
        : this(DefaultVxi11Port, PortmapperPort, usePortmapper: true) { }

    /// <summary>
    /// Creates a backend that connects to a fixed <paramref name="port"/>
    /// without a portmapper round-trip. Intended for tests / co-located
    /// gateways listening on a known loopback port.
    /// </summary>
    public Vxi11Backend(int port)
        : this(port, PortmapperPort, usePortmapper: false) { }

    /// <summary>
    /// Full constructor. When <paramref name="usePortmapper"/> is set the
    /// backend issues a GETPORT against <paramref name="portmapperPort"/> to
    /// learn the Core port, falling back to <paramref name="fallbackPort"/>
    /// if the portmapper is unreachable or has no registration.
    /// </summary>
    public Vxi11Backend(int fallbackPort, int portmapperPort, bool usePortmapper)
    {
        _fallbackPort = fallbackPort;
        _portmapperPort = portmapperPort;
        _usePortmapper = usePortmapper;
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

        var corePort = await ResolveCorePortAsync(tcpip.Host, ct);

        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(tcpip.Host, corePort, ct);
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            client.Dispose();
            return Result.Failure<Unit, BackendError>(
                new TransportDisconnected($"connect failed: {ex.Message}", ex)
            );
        }

        var session = new Vxi11Session(client, device.Name);
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

        // Bring up the Interrupt channel (ADR 0042) so ServiceRequestStream
        // delivers SRQ events. The listener accepts inbound TCP from the
        // gateway and decodes device_intr_srq.
        try
        {
            session.StartInterruptListener();
            await CreateIntrChanAsync(session, ct);
            await DeviceEnableSrqAsync(session, enable: true, ct);
        }
        catch (Exception ex) when (ex is SocketException or IOException or InvalidDataException)
        {
            // SRQ setup failed but SCPI session is fine — log the failure
            // through the BackendError chain on a subsequent stream read.
            session.MarkInterruptSetupFailed(ex);
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
            // Best-effort SRQ teardown before destroying the link.
            if (!session.InterruptSetupFailed)
            {
                try
                {
                    await DeviceEnableSrqAsync(session, enable: false, ct);
                }
                catch
                { /* swallow — session is closing */
                }
                try
                {
                    await DestroyIntrChanAsync(session, ct);
                }
                catch
                { /* swallow */
                }
            }
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
    public async Task<Result<Unit, BackendError>> TriggerAsync(Device device, CancellationToken ct)
    {
        var session = TryGetSession(device);
        if (session is null)
        {
            return Result.Failure<Unit, BackendError>(
                new TransportDisconnected("VXI-11 session not open")
            );
        }
        try
        {
            await DeviceTriggerAsync(session, ct);
            return Result.Success<Unit, BackendError>(Unit.Value);
        }
        catch (Exception ex) when (ex is SocketException or IOException or InvalidDataException)
        {
            return Result.Failure<Unit, BackendError>(
                new TransportDisconnected($"VXI-11 device_trigger failed: {ex.Message}", ex)
            );
        }
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

    private Vxi11Session? TryGetSession(Device device)
    {
        lock (_gate)
        {
            return _sessions.TryGetValue(device.Name, out var s) ? s : null;
        }
    }

    /// <summary>
    /// Resolves the VXI-11 Core TCP port for <paramref name="host"/>. Asks the
    /// portmapper at <see cref="_portmapperPort"/> when enabled, falling back to
    /// <see cref="_fallbackPort"/> if the portmapper is unreachable, times out,
    /// or has no Core registration.
    /// </summary>
    private async Task<int> ResolveCorePortAsync(string host, CancellationToken ct)
    {
        if (!_usePortmapper)
        {
            return _fallbackPort;
        }
        try
        {
            var resolved = await Vxi11Portmapper.ResolveCorePortAsync(
                host,
                _portmapperPort,
                PortmapperProbeTimeout,
                ct
            );
            return resolved > 0 ? resolved : _fallbackPort;
        }
        catch (Exception ex)
            when (ex is SocketException or IOException
                || (ex is OperationCanceledException && !ct.IsCancellationRequested)
            )
        {
            // No reachable portmapper (e.g. co-located gateway) — use the
            // fixed port. Genuine caller cancellation is rethrown.
            return _fallbackPort;
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

    private static async Task DeviceTriggerAsync(Vxi11Session session, CancellationToken ct)
    {
        var call = BuildCall(
            session.NextXid(),
            CoreProgram,
            CoreVersion,
            ProcDeviceTrigger,
            body =>
            {
                body.WriteInt32(session.LinkId);
                body.WriteInt32(0); // flags (0 = no special semantics)
                body.WriteUInt32(5000); // io_timeout
                body.WriteUInt32(0); // lock_timeout
            }
        );
        await Vxi11RecordFraming.WriteRecordAsync(session.Stream, call, ct);
        var reply = SkipReplyHeader(await Vxi11RecordFraming.ReadRecordAsync(session.Stream, ct));
        var error = reply.ReadInt32();
        if (error != Vxi11NoError)
        {
            throw new InvalidDataException($"device_trigger returned error {error}");
        }
    }

    private static async Task CreateIntrChanAsync(Vxi11Session session, CancellationToken ct)
    {
        var call = BuildCall(
            session.NextXid(),
            CoreProgram,
            CoreVersion,
            ProcCreateIntrChan,
            body =>
                Vxi11InterruptCodec.WriteRemoteFunc(
                    body,
                    new DeviceRemoteFunc(
                        HostAddr: session.InterruptHostAddr,
                        HostPort: session.InterruptPort,
                        ProgNum: InterruptProgram,
                        ProgVers: InterruptVersion,
                        ProgFamily: ProgFamilyTcp
                    )
                )
        );
        await Vxi11RecordFraming.WriteRecordAsync(session.Stream, call, ct);
        var reply = SkipReplyHeader(await Vxi11RecordFraming.ReadRecordAsync(session.Stream, ct));
        var error = reply.ReadInt32();
        if (error != Vxi11NoError)
        {
            throw new InvalidDataException($"device_create_intr_chan returned error {error}");
        }
    }

    private static async Task DestroyIntrChanAsync(Vxi11Session session, CancellationToken ct)
    {
        var call = BuildCall(
            session.NextXid(),
            CoreProgram,
            CoreVersion,
            ProcDestroyIntrChan,
            body => { }
        );
        await Vxi11RecordFraming.WriteRecordAsync(session.Stream, call, ct);
        var reply = SkipReplyHeader(await Vxi11RecordFraming.ReadRecordAsync(session.Stream, ct));
        _ = reply.ReadInt32(); // tolerate non-zero error on teardown
    }

    private static async Task DeviceEnableSrqAsync(
        Vxi11Session session,
        bool enable,
        CancellationToken ct
    )
    {
        var call = BuildCall(
            session.NextXid(),
            CoreProgram,
            CoreVersion,
            ProcDeviceEnableSrq,
            body =>
                Vxi11InterruptCodec.WriteEnableSrqParms(
                    body,
                    new DeviceEnableSrqParms(session.LinkId, enable, session.InterruptHandle)
                )
        );
        await Vxi11RecordFraming.WriteRecordAsync(session.Stream, call, ct);
        var reply = SkipReplyHeader(await Vxi11RecordFraming.ReadRecordAsync(session.Stream, ct));
        var error = reply.ReadInt32();
        if (error != Vxi11NoError)
        {
            throw new InvalidDataException($"device_enable_srq returned error {error}");
        }
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
