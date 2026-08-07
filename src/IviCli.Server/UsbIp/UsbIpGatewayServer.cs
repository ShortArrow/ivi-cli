using System.Net;
using System.Net.Sockets;
using System.Text;
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
/// is one emulated USBTMC-USB488 instrument, exported under the route's
/// endpoint as its <c>busid</c> and attached with
/// <c>usbip attach -r &lt;host&gt; -b &lt;busid&gt;</c>.
///
/// The connection carries two protocols in sequence. Before an import it
/// is the op-message protocol — a devlist or an import request, each
/// answered once; after a successful import it is a stream of URBs, and
/// the socket becomes that one device's bus. What a URB <em>means</em>
/// belongs to the layers underneath: endpoint 0 to
/// <see cref="UsbControlPipe"/> with <see cref="UsbTmcControlHandler"/>
/// behind it, the bulk endpoints to <see cref="UsbTmcMessagePump"/>, and
/// the SCPI a complete message turns out to be to the same
/// <see cref="IIviBackend"/> the LAN gateways dispatch to.
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

    /// <summary><c>idProduct</c> of every exported mock — see <see cref="MockVendorId"/>.</summary>
    public const ushort MockProductId = 0x0001;

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
    /// device, hands the connection to that device's URB loop.
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
    /// The URB loop of one imported device: a single reader that decodes
    /// each command, answers it, and completes whatever parked URB the
    /// answer made serviceable. Being single-threaded is the point — the
    /// device state (endpoint 0, the message pump, the parked URBs) needs
    /// no locking, and replies leave in the order the transfers that
    /// caused them were accepted.
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

        try
        {
            var session = new DeviceSession(export.Definition);
            while (!ct.IsCancellationRequested)
            {
                var header = await ReadExactlyAsync(stream, UsbIpConstants.CommandHeaderSize, ct);
                if (header is null)
                {
                    break;
                }

                var probe = new UsbIpCodec.UsbIpReader(header);
                var command = probe.ReadUInt32();

                if (command == UsbIpConstants.CmdSubmit)
                {
                    if (!await SubmitAsync(stream, session, header, device, backend, ct))
                    {
                        break;
                    }
                }
                else if (command == UsbIpConstants.CmdUnlink)
                {
                    await UnlinkAsync(stream, session, header, ct);
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
        finally
        {
            _ = await backend.CloseAsync(device, ct);
        }
    }

    /// <summary>
    /// Handles one USBIP_CMD_SUBMIT. Returns false when the transfer
    /// buffer was cut short, which leaves the stream unusable.
    /// </summary>
    private async Task<bool> SubmitAsync(
        NetworkStream stream,
        DeviceSession session,
        byte[] header,
        Domain.Devices.Device device,
        IIviBackend backend,
        CancellationToken ct
    )
    {
        var reader = new UsbIpCodec.UsbIpReader(header);
        var submit = UsbIpCodec.ReadCmdSubmit(ref reader);

        var payloadLength = UsbIpCodec.CmdSubmitPayloadLength(submit);
        var outPayload = Array.Empty<byte>();
        if (payloadLength > 0)
        {
            var read = await ReadExactlyAsync(stream, payloadLength, ct);
            if (read is null)
            {
                return false;
            }
            outPayload = read;
        }

        var endpoint = submit.Header.Ep;
        var inbound = submit.Header.Direction == UsbIpConstants.DirIn;

        if (endpoint == ControlEndpoint)
        {
            var (reply, payload) = session.Pipe.HandleEp0(
                submit,
                outPayload,
                session.ClassHandler.Handle
            );
            await WriteSubmitReplyAsync(stream, reply, payload, ct);
            return true;
        }

        if (endpoint == BulkEndpoint && !inbound)
        {
            await BulkOutAsync(stream, session, submit, outPayload, device, backend, ct);
            return true;
        }

        if (endpoint == BulkEndpoint && inbound)
        {
            await BulkInAsync(stream, session, submit, ct);
            return true;
        }

        if (endpoint == InterruptEndpoint && inbound)
        {
            // SR0 this phase: nothing raises a service request, so the
            // URB waits. It is still unlinkable, which is the only thing
            // a host that polls anyway depends on.
            session.Park(submit.Header.SeqNum, InterruptEndpoint, submit.TransferBufferLength);
            return true;
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
        return true;
    }

    /// <summary>
    /// One bulk-OUT transfer: through the USBTMC exchange, and — when it
    /// completed a message — through the backend, before the URB is
    /// acknowledged. Dispatching first is what makes the completion mean
    /// what a host reads into it: the device has the message, not merely
    /// the bytes.
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
        var result = session.Pump.SubmitBulkOut(transfer);

        if (result.Outcome == UsbTmcBulkOutOutcome.Rejected)
        {
            _logger.LogWarning("bulk-OUT transfer rejected: {Reason}", result.Reason);
            await WriteSubmitReplyAsync(
                stream,
                Completion(submit.Header.SeqNum, UsbControlPipe.EndpointStalledStatus, 0),
                [],
                ct
            );
            return;
        }

        if (result.Message is { } message)
        {
            await DispatchAsync(session, message, device, backend, ct);
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
        if (!session.HasParked(BulkEndpoint) && session.Pump.TryTakeBulkIn(out var transfer))
        {
            await CompleteBulkInAsync(
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
            && session.Pump.TryTakeBulkIn(out var transfer)
        )
        {
            session.Unpark(parked.SeqNum);
            await CompleteBulkInAsync(stream, parked.SeqNum, transfer, parked.BufferLength, ct);
        }
    }

    private static Task CompleteBulkInAsync(
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
        byte[] header,
        CancellationToken ct
    )
    {
        var reader = new UsbIpCodec.UsbIpReader(header);
        var unlink = UsbIpCodec.ReadCmdUnlink(ref reader);

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
    /// Turns one complete USBTMC message into the SCPI operation it is,
    /// by the rule every gateway here shares: a trailing <c>?</c> makes it
    /// a query, and the answer goes back to the pump for the host's
    /// bulk-IN transfer to collect.
    /// </summary>
    private async Task DispatchAsync(
        DeviceSession session,
        UsbTmcOutboundMessage message,
        Domain.Devices.Device device,
        IIviBackend backend,
        CancellationToken ct
    )
    {
        var text = Encoding.UTF8.GetString(message.Content).TrimEnd('\n').TrimEnd('\r');
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
            session.Pump.SupplyResponse(Encoding.UTF8.GetBytes(response + "\n"));
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

    private const uint BulkEndpoint =
        UsbTmcDeviceProfile.BulkOutEndpointAddress & EndpointNumberMask;

    private const uint InterruptEndpoint =
        UsbTmcDeviceProfile.InterruptInEndpointAddress & EndpointNumberMask;

    private const uint EndpointNumberMask = 0x0F;

    /// <summary>
    /// One route as the protocol sees it: the busid a client attaches by,
    /// the device definition its descriptors come from, and the mock
    /// device its messages reach.
    /// </summary>
    private sealed record ExportedDevice(
        Route Route,
        Domain.Devices.Device Device,
        UsbDeviceDefinition Definition,
        UsbIpExportedDevice Exported
    )
    {
        public string BusId => Route.Endpoint.Value;

        public static ExportedDevice Create(
            Domain.Servers.Server server,
            Route route,
            Domain.Devices.Device device,
            uint ordinal
        )
        {
            var definition = UsbTmcDeviceProfile.Create(
                idVendor: MockVendorId,
                idProduct: MockProductId,
                bcdDevice: MockBcdDevice,
                manufacturer: MockManufacturer,
                product: MockProduct,
                serialNumber: device.Name.Value
            );
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

        private const byte SingleConfiguration = 1;
    }

    /// <summary>
    /// A URB the device accepted but cannot complete yet: the host is
    /// owed either a completion or an unlink answer, and nothing else.
    /// </summary>
    private readonly record struct ParkedUrb(uint SeqNum, uint Endpoint, int BufferLength);

    /// <summary>
    /// Everything one attached device remembers for as long as the attach
    /// lasts. Owned by a single reader loop, hence unsynchronised.
    /// </summary>
    private sealed class DeviceSession
    {
        private readonly List<ParkedUrb> _parked = [];

        public DeviceSession(UsbDeviceDefinition definition)
        {
            Pipe = new UsbControlPipe(definition);
            Pump = new UsbTmcMessagePump();
            Notifier = new Usb488Notifier();
            ClassHandler = new UsbTmcControlHandler(Pump, Notifier);
        }

        public UsbControlPipe Pipe { get; }

        public UsbTmcMessagePump Pump { get; }

        public Usb488Notifier Notifier { get; }

        public UsbTmcControlHandler ClassHandler { get; }

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
}
