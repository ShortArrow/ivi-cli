using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using IviCli.Application.Backends;
using IviCli.Application.Logging;
using IviCli.Application.Servers;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Protocols;
using IviCli.Domain.Scpi;
using IviCli.Domain.Servers;
using Microsoft.Extensions.Logging;

namespace IviCli.Server.UsbIp;

/// <summary>
/// USB/IP device server (ADR 0049 §1): every route bound to this server
/// is one emulated USB instrument, exported under the route's endpoint as
/// its <c>busid</c> and attached with
/// <c>usbip attach -r &lt;host&gt; -b &lt;busid&gt;</c>.
///
/// The connection carries two protocols in sequence. Before an import it
/// is the op-message protocol — a devlist or an import request, each
/// answered once; after a successful import it is a stream of URBs, and
/// the socket becomes that one device's bus. What a URB <em>means</em>
/// belongs to the layers underneath: endpoint 0 to
/// <see cref="UsbControlPipe"/> with a class handler behind it, the bulk
/// endpoints to the profile's exchange, and the SCPI a complete message
/// turns out to be to the same <see cref="IIviBackend"/> the LAN gateways
/// dispatch to.
///
/// Which layers those are is the route's choice
/// (<see cref="UsbExportProfile"/>, ADR 0049 §5): a USBTMC-USB488
/// instrument or a CDC-ACM serial port. The choice is made once per
/// attach, in <see cref="DeviceSession"/>; everything above it — the
/// event loop, the parked URBs, the dispatch rule — is the same for both.
/// </summary>
public sealed class UsbIpGatewayServer : IGatewayServer
{
    /// <summary>
    /// <c>idVendor</c> of every exported mock. 0x1209 is the pid.codes
    /// vendor ID, allocated for open-source and test devices; 0x0001
    /// under it is the block's reserved test product, which no shipped
    /// product may use. A mock instrument is exactly what that
    /// allocation exists for, and it cannot collide with a real
    /// instrument an operator has on the same bench.
    /// </summary>
    public const ushort MockVendorId = 0x1209;

    /// <summary>
    /// <c>idProduct</c> of a mock exported as a USBTMC instrument — see
    /// <see cref="MockVendorId"/>.
    /// </summary>
    public const ushort MockProductId = 0x0001;

    /// <summary>
    /// <c>idProduct</c> of a mock exported as a CDC-ACM serial port. The
    /// pid.codes block reserves 0x0001–0x0010 under
    /// <see cref="MockVendorId"/> for testing, so the same rationale that
    /// gives the instrument 0x0001 gives the serial port the next one.
    /// The two must differ: a host keys its driver store, and Windows its
    /// COM port assignment, on the VID/PID pair, so one pair for two
    /// different device shapes is one device the host cannot tell apart
    /// from the other.
    /// </summary>
    public const ushort MockCdcAcmProductId = 0x0002;

    /// <summary><c>bcdDevice</c>: release 1.00 of the emulated instrument.</summary>
    public const ushort MockBcdDevice = 0x0100;

    /// <summary>The manufacturer string descriptor every exported mock carries.</summary>
    public const string MockManufacturer = "ivi-cli";

    /// <summary>The product string descriptor every exported mock carries.</summary>
    public const string MockProduct = "Mock Instrument";

    /// <summary>
    /// USBIP_RET_UNLINK status for a URB this server actually unlinked:
    /// <c>-ECONNRESET</c>, the errno the Linux USB core reports for a
    /// killed URB. Zero means there was nothing to unlink because the URB
    /// had already completed.
    /// </summary>
    public const int UrbUnlinkedStatus = -104;

    private readonly IBackendFactory _backendFactory;
    private readonly IScenarioBindingRefresher _refresher;
    private readonly ILogger<UsbIpGatewayServer> _logger;

    /// <summary>Creates a new server.</summary>
    public UsbIpGatewayServer(
        IBackendFactory backendFactory,
        ILogger<UsbIpGatewayServer> logger,
        IScenarioBindingRefresher? refresher = null
    )
    {
        _backendFactory = backendFactory;
        _logger = logger;
        _refresher = refresher ?? NullScenarioBindingRefresher.Instance;
    }

    /// <inheritdoc/>
    public ServerType SupportedType => ServerType.UsbIp;

    /// <inheritdoc/>
    public async Task<Result<Unit, GatewayServerError>> RunAsync(
        Domain.Servers.Server server,
        ConfigDocument config,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(server);

        if (!IPAddress.TryParse(server.Bind.Value, out var bindAddr))
        {
            bindAddr = IPAddress.Loopback;
        }
        var listener = new TcpListener(bindAddr, server.Port.Value);
        try
        {
            listener.Start();
        }
        catch (SocketException ex)
        {
            return Result.Failure<Unit, GatewayServerError>(
                new GatewayBindFailure(server.Bind, server.Port, ex.Message, ex)
            );
        }

        var exports = BuildExports(server, config);

        _logger.LogInformation(
            "USB/IP gateway listening on {Bind}:{Port} (server {Name}, {Count} exported device(s): {BusIds})",
            server.Bind.Value,
            server.Port.Value,
            server.Name.Value,
            exports.Count,
            string.Join(", ", exports.Select(e => e.BusId))
        );

        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                // ADR 0015 §3: fire-and-forget per connection; the
                // handler catches everything and logs internally.
                _ = HandleConnectionAsync(client, exports, ct);
            }
        }
        finally
        {
            listener.Stop();
        }

        _logger.LogInformation("USB/IP gateway stopped (server {Name})", server.Name.Value);
        return Result.Success<Unit, GatewayServerError>(Unit.Value);
    }

    /// <summary>
    /// Turns the server's routes into exported devices, in the order the
    /// configuration lists them. The bus and device numbers are that
    /// order, 1-based: nothing on this side is a real bus, so the only
    /// requirement is that <c>devid</c> tells two exports apart and that
    /// a given configuration always yields the same numbers.
    /// </summary>
    private static List<ExportedDevice> BuildExports(
        Domain.Servers.Server server,
        ConfigDocument config
    )
    {
        var exports = new List<ExportedDevice>();
        foreach (var route in config.Routes)
        {
            if (route.ServerName != server.Name)
            {
                continue;
            }

            var device = config.FindDevice(route.DeviceName);
            if (device is null)
            {
                continue;
            }

            exports.Add(
                ExportedDevice.Create(server, route, device, ordinal: (uint)exports.Count + 1)
            );
        }
        return exports;
    }

    private async Task HandleConnectionAsync(
        TcpClient client,
        IReadOnlyList<ExportedDevice> exports,
        CancellationToken ct
    )
    {
        using var scope = _logger.BeginScope(
            new
            {
                Protocol = "usbip",
                RemoteEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown",
            }
        );

        try
        {
            using var tcp = client;
            using var stream = tcp.GetStream();

            var preamble = await ReadExactlyAsync(stream, UsbIpConstants.OpHeaderSize, ct);
            if (preamble is null)
            {
                return;
            }

            var probe = new UsbIpCodec.UsbIpReader(preamble);
            var version = probe.ReadUInt16();
            var code = probe.ReadUInt16();

            switch (code)
            {
                case UsbIpConstants.OpReqDevlist:
                    await SendDevlistAsync(stream, version, exports, ct);
                    break;

                case UsbIpConstants.OpReqImport:
                    await ImportAsync(stream, version, preamble, exports, ct);
                    break;

                default:
                    _logger.LogWarning(
                        "unknown USB/IP op code 0x{Code:X4}; closing connection",
                        code
                    );
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown.
        }
        catch (IOException)
        {
            // The client detached, which is how every attach ends.
            _logger.LogInformation("client detached");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "connection terminated with unexpected error");
        }
    }

    /// <summary>
    /// Answers OP_REQ_DEVLIST with every exported device. The protocol
    /// ends the connection there, so the caller closes it afterwards.
    ///
    /// An attached device is listed like any other. The reply has no
    /// field an in-use marker could go in, and the reference
    /// <c>usbipd</c> lists what the server exports rather than what is
    /// free; a client learns a device is taken by being refused the
    /// import, which is where <see cref="ImportAsync"/> puts it.
    /// </summary>
    private static async Task SendDevlistAsync(
        NetworkStream stream,
        ushort version,
        IReadOnlyList<ExportedDevice> exports,
        CancellationToken ct
    )
    {
        var writer = new UsbIpCodec.UsbIpWriter();
        UsbIpCodec.WriteOpRepDevlist(
            writer,
            new OpRepDevlist(
                Version: version,
                Status: UsbIpConstants.StatusOk,
                Devices: [.. exports.Select(e => e.Exported)]
            )
        );
        await stream.WriteAsync(writer.ToArray(), ct);
    }

    /// <summary>
    /// Answers OP_REQ_IMPORT and, when the busid names an exported
    /// device no one holds, hands the connection to that device's URB
    /// loop.
    ///
    /// One instrument has one owner, so the attach is claimed before the
    /// OK reply is written and released when the URB loop returns. A
    /// busid already attached is refused with the reply an unknown busid
    /// gets: the client commits no port and reports a failed attach,
    /// instead of enumerating a device that would vanish the moment the
    /// backend refused to open twice.
    ///
    /// Connections are handled concurrently, which is why the claim is
    /// an atomic compare-and-exchange rather than a read followed by a
    /// write.
    /// </summary>
    private async Task ImportAsync(
        NetworkStream stream,
        ushort version,
        byte[] preamble,
        IReadOnlyList<ExportedDevice> exports,
        CancellationToken ct
    )
    {
        var busIdField = await ReadExactlyAsync(stream, UsbIpConstants.BusIdSize, ct);
        if (busIdField is null)
        {
            return;
        }

        var reader = new UsbIpCodec.UsbIpReader((byte[])[.. preamble, .. busIdField]);
        var request = UsbIpCodec.ReadOpReqImport(ref reader);
        var export = exports.FirstOrDefault(e =>
            string.Equals(e.BusId, request.BusId, StringComparison.Ordinal)
        );

        if (export is null)
        {
            _logger.LogWarning(
                "import refused: no exported device with busid {BusId}",
                request.BusId
            );
            await WriteImportReplyAsync(stream, version, UsbIpConstants.StatusError, null, ct);
            return;
        }

        if (!export.TryClaimAttach())
        {
            _logger.LogInformation(
                "import refused: device {BusId} is already attached (device {Device})",
                export.BusId,
                export.Device.Name.Value
            );
            await WriteImportReplyAsync(stream, version, UsbIpConstants.StatusError, null, ct);
            return;
        }

        try
        {
            await WriteImportReplyAsync(
                stream,
                version,
                UsbIpConstants.StatusOk,
                export.Exported.Device,
                ct
            );
            _logger.LogInformation(
                "device {BusId} imported (device {Device})",
                export.BusId,
                export.Device.Name.Value
            );

            await ServeAsync(stream, export, ct);
        }
        finally
        {
            export.ReleaseAttach();
        }
    }

    private static async Task WriteImportReplyAsync(
        NetworkStream stream,
        ushort version,
        uint status,
        UsbIpDeviceInfo? device,
        CancellationToken ct
    )
    {
        var writer = new UsbIpCodec.UsbIpWriter();
        UsbIpCodec.WriteOpRepImport(writer, new OpRepImport(version, status, device));
        await stream.WriteAsync(writer.ToArray(), ct);
    }

    /// <summary>
    /// The event loop of one imported device. Two things reach the device
    /// asynchronously and neither may wait on the other: the host's URBs
    /// arrive on the socket, and the backend's service requests arrive on
    /// <see cref="IIviBackend.ServiceRequestStream"/> — a parked
    /// interrupt-IN URB has to complete when one fires even though the
    /// host sent nothing to make it happen.
    ///
    /// So the two producers post to one unbounded channel and a single
    /// consumer drains it. The device state (endpoint 0, the message
    /// pump, the notifier, the parked URBs) has exactly one owner and
    /// needs no locking, every byte written to the socket is written by
    /// that consumer, and replies leave in the order the events that
    /// caused them were accepted — the properties the single reader of
    /// Phase 3b had, kept while gaining a second source of events.
    ///
    /// The attach ends when the socket does: the reader completes the
    /// channel, the consumer runs out of events, and cancelling the
    /// linked source ends the service-request task before the backend is
    /// closed.
    ///
    /// Only a profile with somewhere to deliver a service request
    /// subscribes to them. A CDC-ACM export has none — a COM port carries
    /// no SRQ channel, which is why the SOCKET gateway does not forward
    /// them either — so the forwarder is not started rather than started
    /// and ignored, and the backend sees no subscriber it has to feed.
    /// </summary>
    private async Task ServeAsync(NetworkStream stream, ExportedDevice export, CancellationToken ct)
    {
        var device = export.Device;
        if (Failed(_backendFactory.CreateFor(device), out var backend))
        {
            return;
        }

        if (Failed(await backend.OpenAsync(device, ct), out _))
        {
            return;
        }

        var session = export.CreateSession();
        using var attach = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var events = Channel.CreateUnbounded<DeviceEvent>();
        var commands = Task.Run(
            () => ReadCommandsAsync(stream, events.Writer, attach.Token),
            attach.Token
        );
        var serviceRequests = session.ForwardsServiceRequests
            ? Task.Run(
                () => ForwardServiceRequestsAsync(device, backend, events.Writer, attach.Token),
                attach.Token
            )
            : Task.CompletedTask;

        try
        {
            await ConsumeAsync(stream, events.Reader, session, device, backend, attach.Token);
        }
        finally
        {
            await attach.CancelAsync();
            await SettleAsync(commands);
            await SettleAsync(serviceRequests);
            _ = await backend.CloseAsync(device, ct);
        }
    }

    /// <summary>
    /// The socket half of the event loop: one complete command per
    /// iteration — a header, and for a USBIP_CMD_SUBMIT carrying an OUT
    /// data stage the buffer behind it — posted to the channel and never
    /// answered here. A cut-short buffer, an end of stream, or a command
    /// code the protocol has no room for all end the attach, which is
    /// what completing the channel says.
    /// </summary>
    private async Task ReadCommandsAsync(
        NetworkStream stream,
        ChannelWriter<DeviceEvent> events,
        CancellationToken ct
    )
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var header = await ReadExactlyAsync(stream, UsbIpConstants.CommandHeaderSize, ct);
                if (header is null)
                {
                    break;
                }

                var probe = new UsbIpCodec.UsbIpReader(header);
                var command = probe.ReadUInt32();
                var reader = new UsbIpCodec.UsbIpReader(header);

                if (command == UsbIpConstants.CmdSubmit)
                {
                    var submit = UsbIpCodec.ReadCmdSubmit(ref reader);
                    var payload = await ReadOutPayloadAsync(stream, submit, ct);
                    if (payload is null)
                    {
                        break;
                    }
                    events.TryWrite(new DeviceEvent.Submitted(submit, payload));
                }
                else if (command == UsbIpConstants.CmdUnlink)
                {
                    events.TryWrite(new DeviceEvent.Unlinked(UsbIpCodec.ReadCmdUnlink(ref reader)));
                }
                else
                {
                    _logger.LogWarning(
                        "unknown USB/IP command 0x{Command:X8}; closing connection",
                        command
                    );
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The attach is being torn down.
        }
        catch (IOException)
        {
            _logger.LogInformation("client detached");
        }
        finally
        {
            events.TryComplete();
        }
    }

    /// <summary>
    /// The OUT data stage of a USBIP_CMD_SUBMIT, empty when it has none
    /// and null when the stream ended in the middle of one.
    /// </summary>
    private static async Task<byte[]?> ReadOutPayloadAsync(
        NetworkStream stream,
        UsbIpCmdSubmit submit,
        CancellationToken ct
    )
    {
        var length = UsbIpCodec.CmdSubmitPayloadLength(submit);
        return length > 0 ? await ReadExactlyAsync(stream, length, ct) : [];
    }

    /// <summary>
    /// The backend half of the event loop: every service request the
    /// device raises becomes an event, and the status byte it carries is
    /// what the host will read from the interrupt endpoint and from a
    /// serial poll.
    /// </summary>
    private async Task ForwardServiceRequestsAsync(
        Domain.Devices.Device device,
        IIviBackend backend,
        ChannelWriter<DeviceEvent> events,
        CancellationToken ct
    )
    {
        try
        {
            await foreach (var request in backend.ServiceRequestStream(device, ct))
            {
                events.TryWrite(new DeviceEvent.ServiceRequested(request.StatusByte));
            }
        }
        catch (OperationCanceledException)
        {
            // The attach is being torn down.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "service request stream ended; no further SRQ reaches this host"
            );
        }
    }

    /// <summary>
    /// The single owner of the device: it answers every event and writes
    /// every reply, so nothing below it is ever touched from two threads.
    /// </summary>
    private async Task ConsumeAsync(
        NetworkStream stream,
        ChannelReader<DeviceEvent> events,
        DeviceSession session,
        Domain.Devices.Device device,
        IIviBackend backend,
        CancellationToken ct
    )
    {
        await foreach (var next in events.ReadAllAsync(ct))
        {
            switch (next)
            {
                case DeviceEvent.Submitted submitted:
                    await SubmitAsync(
                        stream,
                        session,
                        submitted.Submit,
                        submitted.OutPayload,
                        device,
                        backend,
                        ct
                    );
                    break;

                case DeviceEvent.Unlinked unlinked:
                    await UnlinkAsync(stream, session, unlinked.Unlink, ct);
                    break;

                case DeviceEvent.ServiceRequested raised:
                    session.RaiseServiceRequest(raised.StatusByte);
                    await ServeParkedInterruptAsync(stream, session, ct);
                    break;
            }
        }
    }

    /// <summary>
    /// Answers one USBIP_CMD_SUBMIT, on whichever endpoint it addresses.
    /// </summary>
    private async Task SubmitAsync(
        NetworkStream stream,
        DeviceSession session,
        UsbIpCmdSubmit submit,
        byte[] outPayload,
        Domain.Devices.Device device,
        IIviBackend backend,
        CancellationToken ct
    )
    {
        var endpoint = submit.Header.Ep;
        var inbound = submit.Header.Direction == UsbIpConstants.DirIn;

        if (endpoint == ControlEndpoint)
        {
            var (reply, payload) = session.HandleControl(submit, outPayload);
            await WriteSubmitReplyAsync(stream, reply, payload, ct);
            // READ_STATUS_BYTE queues a notification of its own, so the
            // interrupt endpoint is served after every control transfer.
            await ServeParkedInterruptAsync(stream, session, ct);
            return;
        }

        if (endpoint == BulkEndpoint && !inbound)
        {
            await BulkOutAsync(stream, session, submit, outPayload, device, backend, ct);
            return;
        }

        if (endpoint == BulkEndpoint && inbound)
        {
            await BulkInAsync(stream, session, submit, ct);
            return;
        }

        if (endpoint == InterruptEndpoint && inbound)
        {
            // Parked first and served immediately after, so a URB that
            // arrives when a notification is already waiting completes at
            // once and one that arrives before waits in order.
            session.Park(submit.Header.SeqNum, InterruptEndpoint, submit.TransferBufferLength);
            await ServeParkedInterruptAsync(stream, session, ct);
            return;
        }

        _logger.LogWarning(
            "URB for endpoint {Endpoint} direction {Direction} has no device endpoint; stalling",
            endpoint,
            submit.Header.Direction
        );
        await WriteSubmitReplyAsync(
            stream,
            Completion(submit.Header.SeqNum, UsbControlPipe.EndpointStalledStatus, 0),
            [],
            ct
        );
    }

    /// <summary>
    /// Waits for one of the attach's helper tasks to finish after the
    /// attach was cancelled, where an abandoned socket read or an
    /// interrupted stream is the ordinary ending rather than a fault.
    /// </summary>
    private async Task SettleAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // The teardown this method exists to perform.
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "attach helper task ended with an error");
        }
    }

    /// <summary>
    /// One bulk-OUT transfer: through the profile's exchange, and — for
    /// every message it completed, and for a trigger it asked for —
    /// through the backend, before the URB is acknowledged. Dispatching
    /// first is what makes the completion mean what a host reads into it:
    /// the device has the message, not merely the bytes.
    /// </summary>
    private async Task BulkOutAsync(
        NetworkStream stream,
        DeviceSession session,
        UsbIpCmdSubmit submit,
        byte[] transfer,
        Domain.Devices.Device device,
        IIviBackend backend,
        CancellationToken ct
    )
    {
        var outcome = session.SubmitBulkOut(transfer);

        if (outcome.Rejection is { } reason)
        {
            _logger.LogWarning("bulk-OUT transfer rejected: {Reason}", reason);
            await WriteSubmitReplyAsync(
                stream,
                Completion(submit.Header.SeqNum, UsbControlPipe.EndpointStalledStatus, 0),
                [],
                ct
            );
            return;
        }

        foreach (var message in outcome.Messages)
        {
            await DispatchAsync(session, message, device, backend, ct);
        }

        if (outcome.TriggerRequested)
        {
            _ = Failed(await backend.TriggerAsync(device, ct), out _);
        }

        await WriteSubmitReplyAsync(
            stream,
            Completion(submit.Header.SeqNum, 0, transfer.Length),
            [],
            ct
        );
        await ServeParkedBulkInAsync(stream, session, ct);
    }

    /// <summary>
    /// One bulk-IN transfer. A host may queue it before the answer it
    /// asks for exists, so an unserviceable URB is parked rather than
    /// completed empty; <see cref="ServeParkedBulkInAsync"/> finishes it
    /// as soon as the OUT side has produced something.
    /// </summary>
    private static async Task BulkInAsync(
        NetworkStream stream,
        DeviceSession session,
        UsbIpCmdSubmit submit,
        CancellationToken ct
    )
    {
        if (
            !session.HasParked(BulkEndpoint)
            && session.TryTakeBulkIn(submit.TransferBufferLength, out var transfer)
        )
        {
            await CompleteInAsync(
                stream,
                submit.Header.SeqNum,
                transfer,
                submit.TransferBufferLength,
                ct
            );
            return;
        }

        session.Park(submit.Header.SeqNum, BulkEndpoint, submit.TransferBufferLength);
    }

    private static async Task ServeParkedBulkInAsync(
        NetworkStream stream,
        DeviceSession session,
        CancellationToken ct
    )
    {
        while (
            session.TryPeekParked(BulkEndpoint, out var parked)
            && session.TryTakeBulkIn(parked.BufferLength, out var transfer)
        )
        {
            session.Unpark(parked.SeqNum);
            await CompleteInAsync(stream, parked.SeqNum, transfer, parked.BufferLength, ct);
        }
    }

    /// <summary>
    /// Hands the notifications waiting for the interrupt endpoint to the
    /// URBs waiting to carry them, oldest first. Called after everything
    /// that can queue one — a service request from the backend and a
    /// READ_STATUS_BYTE from the host — so a notification is never left
    /// sitting behind a URB that could have taken it.
    ///
    /// A profile that queues none leaves every such URB parked until the
    /// host unlinks it, which is what a CDC-ACM notification endpoint
    /// with no SERIAL_STATE to report is supposed to look like.
    /// </summary>
    private static async Task ServeParkedInterruptAsync(
        NetworkStream stream,
        DeviceSession session,
        CancellationToken ct
    )
    {
        while (
            session.TryPeekParked(InterruptEndpoint, out var parked)
            && session.TryTakeNotification(out var packet)
        )
        {
            session.Unpark(parked.SeqNum);
            await CompleteInAsync(stream, parked.SeqNum, packet, parked.BufferLength, ct);
        }
    }

    /// <summary>
    /// Completes one IN URB with the bytes it carries, cut to the buffer
    /// the host offered.
    /// </summary>
    private static Task CompleteInAsync(
        NetworkStream stream,
        uint seqNum,
        byte[] transfer,
        int bufferLength,
        CancellationToken ct
    )
    {
        var actual = Math.Min(transfer.Length, bufferLength);
        return WriteSubmitReplyAsync(
            stream,
            Completion(seqNum, 0, actual),
            transfer.AsSpan(0, actual).ToArray(),
            ct
        );
    }

    /// <summary>
    /// USBIP_CMD_UNLINK. A parked URB is dropped and reported as killed;
    /// one that already completed is reported as nothing-to-do. Either
    /// way the unlinked URB never gets a USBIP_RET_SUBMIT, which is what
    /// the protocol requires of a successful unlink.
    /// </summary>
    private static async Task UnlinkAsync(
        NetworkStream stream,
        DeviceSession session,
        UsbIpCmdUnlink unlink,
        CancellationToken ct
    )
    {
        var writer = new UsbIpCodec.UsbIpWriter();
        UsbIpCodec.WriteRetUnlink(
            writer,
            new UsbIpRetUnlink(
                Header: ReplyHeader(UsbIpConstants.RetUnlink, unlink.Header.SeqNum),
                Status: session.Unpark(unlink.UnlinkSeqNum) ? UrbUnlinkedStatus : 0
            )
        );
        await stream.WriteAsync(writer.ToArray(), ct);
    }

    /// <summary>
    /// Turns one complete message into the SCPI operation it is, by the
    /// rule every gateway here shares: a blank one is nothing, a trailing
    /// <c>?</c> makes it a query, and the answer goes back to the
    /// profile's exchange for the host's bulk-IN transfer to collect,
    /// newline included.
    /// </summary>
    private async Task DispatchAsync(
        DeviceSession session,
        byte[] message,
        Domain.Devices.Device device,
        IIviBackend backend,
        CancellationToken ct
    )
    {
        var text = Encoding.UTF8.GetString(message).TrimEnd('\n').TrimEnd('\r');
        if (text.Length == 0)
        {
            return;
        }

        // Pick up an out-of-process scenario re-binding mid-attach: a
        // host may hold one attach for hours while a separate
        // `mock scenario activate` runs. The refresher re-applies only
        // when the bound scenario name changed and is no-throw; guard
        // anyway so a refresh failure never kills the attach.
        try
        {
            await _refresher.RefreshAsync(device, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "scenario binding refresh failed; continuing");
        }

        if (text.EndsWith('?'))
        {
            if (Failed(ScpiQuery.From(text), out var query))
            {
                return;
            }
            if (Failed(await backend.QueryAsync(device, query, ct), out var response))
            {
                return;
            }
            session.SupplyResponse(Encoding.UTF8.GetBytes(response + "\n"));
            return;
        }

        if (Failed(ScpiCommand.From(text), out var command))
        {
            return;
        }
        _ = Failed(await backend.WriteAsync(device, command, ct), out _);
    }

    private static Task WriteSubmitReplyAsync(
        NetworkStream stream,
        UsbIpRetSubmit reply,
        byte[] payload,
        CancellationToken ct
    )
    {
        var writer = new UsbIpCodec.UsbIpWriter();
        UsbIpCodec.WriteRetSubmit(writer, reply);
        writer.WriteBytes(payload);
        return stream.WriteAsync(writer.ToArray(), ct).AsTask();
    }

    private static UsbIpRetSubmit Completion(uint seqNum, int status, int actualLength) =>
        new(
            Header: ReplyHeader(UsbIpConstants.RetSubmit, seqNum),
            Status: status,
            ActualLength: actualLength,
            StartFrame: 0,
            NumberOfPackets: UsbIpConstants.NumberOfPacketsNonIso,
            ErrorCount: 0
        );

    /// <summary>
    /// The header every server-side message carries: the request's
    /// <c>seqnum</c> echoed, and <c>devid</c>, <c>direction</c> and
    /// <c>ep</c> zeroed as the protocol requires.
    /// </summary>
    private static UsbIpHeaderBasic ReplyHeader(uint command, uint seqNum) =>
        new(Command: command, SeqNum: seqNum, DevId: 0, Direction: 0, Ep: 0);

    private static async Task<byte[]?> ReadExactlyAsync(
        NetworkStream stream,
        int count,
        CancellationToken ct
    )
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), ct);
            if (read == 0)
            {
                return null;
            }
            offset += read;
        }
        return buffer;
    }

    /// <summary>
    /// Unwraps a result, logging the error side through the contract it
    /// carries (severity, message template, structured arguments, cause).
    /// </summary>
    /// <returns><c>true</c> when the result failed and the caller should stop.</returns>
    private bool Failed<T, TError>(Result<T, TError> result, out T value)
        where TError : IviError
    {
        if (result is Result<T, TError>.Ok ok)
        {
            value = ok.Value;
            return false;
        }

        if (result is Result<T, TError>.Error error)
        {
            _logger.LogIviError(error.Err);
        }

        value = default!;
        return true;
    }

    private const uint ControlEndpoint = 0;

    /// <summary>
    /// The endpoint number both bulk pipes carry. A URB is dispatched by
    /// endpoint before the profile is consulted, which is only sound
    /// because the two profiles picked the same numbers independently —
    /// endpoint 1 for the bulk pair, endpoint 2 for the interrupt-IN.
    /// A profile that moved either would need this dispatch to become
    /// profile-aware; <c>CdcAcmExportTests</c> fails first if one does.
    /// </summary>
    private const uint BulkEndpoint =
        UsbTmcDeviceProfile.BulkOutEndpointAddress & EndpointNumberMask;

    /// <summary>The endpoint number both profiles notify on — see <see cref="BulkEndpoint"/>.</summary>
    private const uint InterruptEndpoint =
        UsbTmcDeviceProfile.InterruptInEndpointAddress & EndpointNumberMask;

    private const uint EndpointNumberMask = 0x0F;

    /// <summary>
    /// One route as the protocol sees it: the busid a client attaches by,
    /// the device definition its descriptors come from, and the mock
    /// device its messages reach.
    ///
    /// One instance exists per busid for the life of the server, which is
    /// what lets it hold the attach claim — the only state here that two
    /// connections ever touch at once.
    /// </summary>
    private sealed record ExportedDevice(
        Route Route,
        Domain.Devices.Device Device,
        UsbDeviceDefinition Definition,
        UsbIpExportedDevice Exported
    )
    {
        private int _attached;

        public string BusId => Route.Endpoint.Value;

        /// <summary>
        /// Takes the attach, or reports that another connection already
        /// holds it. Only the caller that took it may
        /// <see cref="ReleaseAttach"/>.
        /// </summary>
        public bool TryClaimAttach() => Interlocked.CompareExchange(ref _attached, 1, 0) == 0;

        /// <summary>Hands the busid back, so a later import may have it.</summary>
        public void ReleaseAttach() => Interlocked.Exchange(ref _attached, 0);

        /// <summary>
        /// The state one attach of this device owns, built for the
        /// profile the route declared.
        /// </summary>
        public DeviceSession CreateSession() =>
            Route.Profile switch
            {
                UsbExportProfile.CdcAcm => new CdcAcmSession(Definition),
                _ => new UsbTmcSession(Definition),
            };

        public static ExportedDevice Create(
            Domain.Servers.Server server,
            Route route,
            Domain.Devices.Device device,
            uint ordinal
        )
        {
            var definition = Define(route.Profile, device.Name.Value);
            var configuration = definition.Configuration;

            var info = new UsbIpDeviceInfo(
                Path: $"/ivi-cli/{server.Name.Value}/{route.Endpoint.Value}",
                BusId: route.Endpoint.Value,
                BusNum: ordinal,
                DevNum: ordinal,
                // High speed, because the profile's descriptors declare
                // the packet sizes USB 2.0 allows at high speed only.
                Speed: UsbIpConstants.SpeedHigh,
                IdVendor: definition.IdVendor,
                IdProduct: definition.IdProduct,
                BcdDevice: definition.BcdDevice,
                DeviceClass: definition.DeviceClass,
                DeviceSubClass: definition.DeviceSubClass,
                DeviceProtocol: definition.DeviceProtocol,
                ConfigurationValue: configuration.ConfigurationValue,
                NumConfigurations: SingleConfiguration,
                NumInterfaces: (byte)configuration.Interfaces.Count
            );

            var interfaces = configuration
                .Interfaces.Select(i => new UsbIpInterfaceInfo(
                    i.InterfaceClass,
                    i.InterfaceSubClass,
                    i.InterfaceProtocol
                ))
                .ToArray();

            return new ExportedDevice(
                route,
                device,
                definition,
                new UsbIpExportedDevice(info, interfaces)
            );
        }

        /// <summary>
        /// The descriptors of one exported mock. Manufacturer and product
        /// are the same either way — it is the same mock — and the serial
        /// number stays the device's own name, which is what a host keys
        /// a COM port number or a VISA resource to. Only the product ID
        /// and the descriptor tree differ, and those are what decide
        /// which driver binds.
        /// </summary>
        private static UsbDeviceDefinition Define(UsbExportProfile profile, string serialNumber) =>
            profile switch
            {
                UsbExportProfile.CdcAcm => CdcAcmDeviceProfile.Create(
                    idVendor: MockVendorId,
                    idProduct: MockCdcAcmProductId,
                    bcdDevice: MockBcdDevice,
                    manufacturer: MockManufacturer,
                    product: MockProduct,
                    serialNumber: serialNumber
                ),
                _ => UsbTmcDeviceProfile.Create(
                    idVendor: MockVendorId,
                    idProduct: MockProductId,
                    bcdDevice: MockBcdDevice,
                    manufacturer: MockManufacturer,
                    product: MockProduct,
                    serialNumber: serialNumber
                ),
            };

        private const byte SingleConfiguration = 1;
    }

    /// <summary>
    /// A URB the device accepted but cannot complete yet: the host is
    /// owed either a completion or an unlink answer, and nothing else.
    /// </summary>
    private readonly record struct ParkedUrb(uint SeqNum, uint Endpoint, int BufferLength);

    /// <summary>
    /// Something the device has to answer. The two producers of the
    /// event loop put these in the channel and
    /// <see cref="ConsumeAsync"/> takes them out: the socket reader posts
    /// the host's commands, the backend forwarder posts the instrument's
    /// service requests, and the ordering between them is whatever
    /// arrived first.
    /// </summary>
    private abstract record DeviceEvent
    {
        /// <summary>A USBIP_CMD_SUBMIT with its OUT data stage, if any.</summary>
        public sealed record Submitted(UsbIpCmdSubmit Submit, byte[] OutPayload) : DeviceEvent;

        /// <summary>A USBIP_CMD_UNLINK.</summary>
        public sealed record Unlinked(UsbIpCmdUnlink Unlink) : DeviceEvent;

        /// <summary>
        /// A service request the backend raised, with the status byte a
        /// serial poll will report.
        /// </summary>
        public sealed record ServiceRequested(byte StatusByte) : DeviceEvent;
    }

    /// <summary>
    /// What one bulk-OUT transfer asks the event loop to do. Shaped by
    /// the loop rather than by either profile: whether to stall, which
    /// complete messages to dispatch before acknowledging the URB, and
    /// whether the transfer was itself a trigger.
    /// </summary>
    private readonly record struct BulkOutOutcome(
        IReadOnlyList<byte[]> Messages,
        bool TriggerRequested,
        string? Rejection
    )
    {
        /// <summary>The transfer is malformed for the profile; the endpoint stalls.</summary>
        public static BulkOutOutcome Rejected(string reason) => new([], false, reason);

        /// <summary>The transfer was taken, and closed the messages it names.</summary>
        public static BulkOutOutcome Accepted(
            IReadOnlyList<byte[]> messages,
            bool triggerRequested = false
        ) => new(messages, triggerRequested, null);
    }

    /// <summary>
    /// Everything one attached device remembers for as long as the attach
    /// lasts, and every question the event loop's single consumer asks
    /// it. Owned by that consumer, hence unsynchronised.
    ///
    /// The shape is the loop's, not either profile's: endpoint 0, a
    /// bulk-OUT that says what to dispatch, a bulk-IN that hands over
    /// whatever it holds, the interrupt endpoint, and the intake for a
    /// service request. The parked URBs belong to the USB/IP protocol
    /// rather than to any profile, so they live here once and neither
    /// implementation restates them.
    /// </summary>
    private abstract class DeviceSession
    {
        private readonly List<ParkedUrb> _parked = [];

        protected DeviceSession(UsbDeviceDefinition definition)
        {
            Pipe = new UsbControlPipe(definition);
        }

        /// <summary>The endpoint-0 state machine, shared by both profiles.</summary>
        protected UsbControlPipe Pipe { get; }

        /// <summary>
        /// Whether this attach subscribes to the backend's service
        /// requests. A profile with no way to deliver one says no, and
        /// the forwarder is never started.
        /// </summary>
        public abstract bool ForwardsServiceRequests { get; }

        /// <summary>Answers one control transfer, class requests included.</summary>
        public abstract (UsbIpRetSubmit Reply, byte[] Payload) HandleControl(
            UsbIpCmdSubmit submit,
            byte[] outPayload
        );

        /// <summary>Feeds one bulk-OUT transfer through the profile's exchange.</summary>
        public abstract BulkOutOutcome SubmitBulkOut(byte[] transfer);

        /// <summary>Hands the exchange the bytes that answer the host.</summary>
        public abstract void SupplyResponse(byte[] response);

        /// <summary>
        /// Takes what a bulk-IN URB of <paramref name="maxLength"/> bytes
        /// can carry; false when there is nothing, which is when the URB
        /// parks.
        /// </summary>
        public abstract bool TryTakeBulkIn(int maxLength, out byte[] transfer);

        /// <summary>Records a service request the backend raised.</summary>
        public abstract void RaiseServiceRequest(byte statusByte);

        /// <summary>
        /// Takes the next packet the interrupt endpoint owes the host;
        /// false when none is queued.
        /// </summary>
        public abstract bool TryTakeNotification(out byte[] packet);

        public void Park(uint seqNum, uint endpoint, int bufferLength) =>
            _parked.Add(new ParkedUrb(seqNum, endpoint, bufferLength));

        public bool HasParked(uint endpoint) => _parked.Exists(u => u.Endpoint == endpoint);

        /// <summary>The longest-waiting URB on <paramref name="endpoint"/>.</summary>
        public bool TryPeekParked(uint endpoint, out ParkedUrb urb)
        {
            var index = _parked.FindIndex(u => u.Endpoint == endpoint);
            urb = index < 0 ? default : _parked[index];
            return index >= 0;
        }

        /// <summary>Drops a parked URB; false when it is not (or no longer) parked.</summary>
        public bool Unpark(uint seqNum) => _parked.RemoveAll(u => u.SeqNum == seqNum) > 0;
    }

    /// <summary>
    /// The USBTMC-USB488 attach (ADR 0049 §2): bulk framing through
    /// <see cref="UsbTmcMessagePump"/>, the class requests through
    /// <see cref="UsbTmcControlHandler"/>, and SRQ notifications through
    /// <see cref="Usb488Notifier"/> — the profile that declares SR1, and
    /// therefore the one that wants the backend's service requests.
    /// </summary>
    private sealed class UsbTmcSession : DeviceSession
    {
        private readonly UsbTmcMessagePump _pump = new();
        private readonly Usb488Notifier _notifier = new();
        private readonly UsbTmcControlHandler _classHandler;

        public UsbTmcSession(UsbDeviceDefinition definition)
            : base(definition)
        {
            _classHandler = new UsbTmcControlHandler(_pump, _notifier);
        }

        public override bool ForwardsServiceRequests => true;

        public override (UsbIpRetSubmit Reply, byte[] Payload) HandleControl(
            UsbIpCmdSubmit submit,
            byte[] outPayload
        ) => Pipe.HandleEp0(submit, outPayload, _classHandler.Handle);

        /// <summary>
        /// One USBTMC transfer carries at most one complete message, so
        /// the list the loop dispatches is empty or a single element.
        /// </summary>
        public override BulkOutOutcome SubmitBulkOut(byte[] transfer)
        {
            var result = _pump.SubmitBulkOut(transfer);
            return result.Outcome == UsbTmcBulkOutOutcome.Rejected
                ? BulkOutOutcome.Rejected(result.Reason ?? string.Empty)
                : BulkOutOutcome.Accepted(
                    result.Message is { } message ? [message.Content] : [],
                    result.Outcome == UsbTmcBulkOutOutcome.TriggerRequested
                );
        }

        public override void SupplyResponse(byte[] response) => _pump.SupplyResponse(response);

        /// <summary>
        /// The transfer size is the one REQUEST_DEV_DEP_MSG_IN named, so
        /// the pump has already cut the message to it and the URB's own
        /// buffer length decides nothing here.
        /// </summary>
        public override bool TryTakeBulkIn(int maxLength, out byte[] transfer) =>
            _pump.TryTakeBulkIn(out transfer);

        public override void RaiseServiceRequest(byte statusByte) =>
            _notifier.RaiseServiceRequest(statusByte);

        public override bool TryTakeNotification(out byte[] packet) =>
            _notifier.TryTakeNotification(out packet);
    }

    /// <summary>
    /// The CDC-ACM attach (ADR 0049 §5): a byte stream on the bulk pair
    /// through <see cref="CdcAcmStreamPump"/>, the PSTN class requests
    /// through <see cref="CdcAcmControlHandler"/>, and nothing at all on
    /// the notification endpoint.
    ///
    /// The dispatch rule is the SOCKET gateway's, because what a terminal
    /// sends is what a raw TCP client sends: every closed line is a
    /// message, a blank one is nothing, and the caller decides which is
    /// which. There is no SRQ channel on a COM port — a serial device
    /// would signal one through SERIAL_STATE, which this profile does not
    /// claim and does not send — so service requests are not subscribed
    /// to at all rather than raised into a queue no URB ever drains.
    /// </summary>
    private sealed class CdcAcmSession : DeviceSession
    {
        private readonly CdcAcmStreamPump _pump = new();
        private readonly CdcAcmControlHandler _classHandler = new();

        public CdcAcmSession(UsbDeviceDefinition definition)
            : base(definition) { }

        public override bool ForwardsServiceRequests => false;

        /// <summary>
        /// Answers the transfer, then reads DTR for the one thing this
        /// profile treats as a session boundary. A terminal raises DTR on
        /// opening the port and drops it on closing, so the falling edge
        /// is where the previous session ends: the half-line nobody
        /// terminated and the answer no transfer collected are that
        /// session's, and the next one must not read them.
        ///
        /// The edge, not the level. A driver re-issues
        /// SET_CONTROL_LINE_STATE whenever anything about the lines
        /// changes — raising RTS, for one — and clearing on every request
        /// that finds DTR low would be indistinguishable from clearing
        /// mid-session.
        ///
        /// A bulk-IN URB already parked when the line falls is left
        /// parked. Completing it holds nothing to complete it with, and a
        /// zero-length completion is what a host reads as a successful
        /// short read rather than as a closed port; leaving it
        /// outstanding is what a read on a modem that lost its carrier
        /// does, and the host still has USBIP_CMD_UNLINK.
        /// </summary>
        public override (UsbIpRetSubmit Reply, byte[] Payload) HandleControl(
            UsbIpCmdSubmit submit,
            byte[] outPayload
        )
        {
            var wasReady = _classHandler.DataTerminalReady;
            var answer = Pipe.HandleEp0(submit, outPayload, _classHandler.Handle);
            if (wasReady && !_classHandler.DataTerminalReady)
            {
                _pump.Clear();
            }
            return answer;
        }

        /// <summary>
        /// A stream has no framing to reject, so every transfer is taken;
        /// one may close several lines, or none.
        /// </summary>
        public override BulkOutOutcome SubmitBulkOut(byte[] transfer) =>
            BulkOutOutcome.Accepted(_pump.SubmitBulkOut(transfer));

        public override void SupplyResponse(byte[] response) => _pump.SupplyResponse(response);

        public override bool TryTakeBulkIn(int maxLength, out byte[] transfer) =>
            _pump.TryTakeBulkIn(maxLength, out transfer);

        /// <summary>
        /// Unreachable: <see cref="ForwardsServiceRequests"/> is false, so
        /// no service request ever becomes an event on this attach.
        /// </summary>
        public override void RaiseServiceRequest(byte statusByte) { }

        /// <summary>
        /// Always false. The notification endpoint exists because CDC 1.1
        /// §3.3.1 requires the communications interface to own one, and
        /// this device raises no SERIAL_STATE, so a URB submitted to it
        /// waits until the host unlinks it — which is what a host that
        /// polls an idle modem-status endpoint expects to happen.
        /// </summary>
        public override bool TryTakeNotification(out byte[] packet)
        {
            packet = [];
            return false;
        }
    }
}
