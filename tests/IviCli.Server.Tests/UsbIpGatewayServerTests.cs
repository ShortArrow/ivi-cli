using System.Net;
using System.Net.Sockets;
using System.Text;
using IviCli.Backends.Fake;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Protocols;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.Server.UsbIp;
using IviCli.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace IviCli.Server.Tests;

/// <summary>
/// The USB/IP device server of ADR 0049 §1 exercised the way the kernel
/// client exercises it, minus the kernel: the test speaks the wire
/// protocol over a real TCP connection with the Phase 1 codec, so
/// everything between <c>OP_REQ_DEVLIST</c> and a SCPI answer coming back
/// as <c>DEV_DEP_MSG_IN</c> is covered without an attach — which is what
/// the ADR's Verification section asks CI to do.
/// </summary>
public sealed class UsbIpGatewayServerTests
{
    private const string BusId = "1-1";
    private const string IdnResponse = "FAKE,USBIP,0,1.0";

    [Fact]
    public async Task Devlist_lists_the_configured_route_as_a_usbtmc_device()
    {
        await using var bench = await Bench.StartAsync();

        var reply = await bench.Connect().RequestDevlistAsync(bench.Token);

        reply.Status.ShouldBe(UsbIpConstants.StatusOk);
        var exported = reply.Devices.ShouldHaveSingleItem();
        exported.Device.BusId.ShouldBe(BusId);
        exported.Device.BusNum.ShouldBe(1u);
        exported.Device.DevNum.ShouldBe(1u);
        exported.Device.Speed.ShouldBe(UsbIpConstants.SpeedHigh);
        exported.Device.IdVendor.ShouldBe(UsbIpGatewayServer.MockVendorId);
        exported.Device.IdProduct.ShouldBe(UsbIpGatewayServer.MockProductId);
        exported.Device.NumInterfaces.ShouldBe((byte)1);

        var descriptor = exported.Interfaces.ShouldHaveSingleItem();
        descriptor.InterfaceClass.ShouldBe(UsbTmcConstants.InterfaceClass);
        descriptor.InterfaceSubClass.ShouldBe(UsbTmcConstants.InterfaceSubClass);
        descriptor.InterfaceProtocol.ShouldBe(UsbTmcConstants.InterfaceProtocolUsb488);
    }

    [Fact]
    public async Task Import_of_a_configured_busid_succeeds()
    {
        await using var bench = await Bench.StartAsync();

        var reply = await bench.Connect().RequestImportAsync(BusId, bench.Token);

        reply.Status.ShouldBe(UsbIpConstants.StatusOk);
        reply.Device.ShouldNotBeNull().BusId.ShouldBe(BusId);
    }

    [Fact]
    public async Task Import_of_an_unknown_busid_answers_an_error_status()
    {
        await using var bench = await Bench.StartAsync();

        var reply = await bench.Connect().RequestImportAsync("9-9", bench.Token);

        reply.Status.ShouldBe(UsbIpConstants.StatusError);
        reply.Device.ShouldBeNull();
    }

    [Fact]
    public async Task An_imported_device_enumerates_over_endpoint_zero()
    {
        await using var bench = await Bench.StartAsync();
        var client = await bench.ImportAsync();

        var descriptor = await client.ControlInAsync(
            UsbIpTestClient.DeviceToHostStandardDevice,
            UsbStandardRequest.GetDescriptor,
            wValue: UsbDescriptorType.Device << 8,
            wIndex: 0,
            wLength: 64,
            bench.Token
        );
        descriptor.Reply.Status.ShouldBe(0);
        descriptor.Payload.Length.ShouldBe(UsbDescriptors.DeviceDescriptorLength);
        BitConverter.ToUInt16(descriptor.Payload, 8).ShouldBe(UsbIpGatewayServer.MockVendorId);

        var configured = await client.ControlOutAsync(
            UsbIpTestClient.HostToDeviceStandardDevice,
            UsbStandardRequest.SetConfiguration,
            wValue: UsbTmcDeviceProfile.ConfigurationValue,
            wIndex: 0,
            bench.Token
        );
        configured.Reply.Status.ShouldBe(0);
        configured.Reply.ActualLength.ShouldBe(0);

        var capabilities = await client.ControlInAsync(
            UsbIpTestClient.DeviceToHostClassInterface,
            UsbTmcConstants.RequestGetCapabilities,
            wValue: 0,
            wIndex: UsbTmcDeviceProfile.InterfaceNumber,
            wLength: UsbTmcConstants.CapabilitiesResponseSize,
            bench.Token
        );
        capabilities.Reply.Status.ShouldBe(0);
        capabilities.Payload.Length.ShouldBe(UsbTmcConstants.CapabilitiesResponseSize);
        capabilities.Payload[0].ShouldBe(UsbTmcConstants.StatusSuccess);
        // bmIntfcCapabilities488: the interface accepts TRIGGER.
        capabilities.Payload[14].ShouldBe(UsbTmcConstants.Interface488CapabilityTrigger);
        // bmDevCapabilities488: SR1 and DT1, so a host subscribes to the
        // interrupt endpoint and may trigger the device.
        capabilities
            .Payload[15]
            .ShouldBe(
                (byte)(
                    UsbTmcConstants.Device488CapabilitySr1 | UsbTmcConstants.Device488CapabilityDt1
                )
            );
    }

    [Fact]
    public async Task A_query_travels_out_as_a_usbtmc_message_and_back_as_the_scenario_answer()
    {
        await using var bench = await Bench.StartAsync();
        var client = await bench.ImportAsync();

        var written = await client.BulkOutAsync(
            UsbTmcCodec.WriteDevDepMsgOut(
                new UsbTmcDevDepMsgOut(
                    BTag: 1,
                    EndOfMessage: true,
                    Encoding.ASCII.GetBytes("*IDN?\n")
                )
            ),
            bench.Token
        );
        written.Reply.Status.ShouldBe(0);

        var requested = await client.BulkOutAsync(
            UsbTmcCodec.WriteRequestDevDepMsgIn(
                new UsbTmcRequestDevDepMsgIn(
                    BTag: 2,
                    TransferSize: 1024,
                    TermCharEnabled: false,
                    TermChar: 0
                )
            ),
            bench.Token
        );
        requested.Reply.Status.ShouldBe(0);

        var answer = await client.BulkInAsync(bufferLength: 1024, bench.Token);
        answer.Reply.Status.ShouldBe(0);

        var message = UsbTmcCodec.ReadDevDepMsgIn(answer.Payload);
        message.BTag.ShouldBe((byte)2);
        message.EndOfMessage.ShouldBeTrue();
        Encoding.ASCII.GetString(message.Payload).ShouldBe(IdnResponse + "\n");
    }

    [Fact]
    public async Task A_bulk_in_urb_submitted_before_the_answer_exists_completes_once_it_does()
    {
        await using var bench = await Bench.StartAsync();
        var client = await bench.ImportAsync();

        // The host queues the IN URB first — the ordering the Linux
        // client actually uses, and the one a device that answers only
        // what it has already been asked would deadlock on.
        var parked = client.SubmitBulkIn(bufferLength: 1024);
        var outbound = client.SubmitBulkOut(
            UsbTmcCodec.WriteDevDepMsgOut(
                new UsbTmcDevDepMsgOut(
                    BTag: 1,
                    EndOfMessage: true,
                    Encoding.ASCII.GetBytes("*IDN?\n")
                )
            )
        );
        var request = client.SubmitBulkOut(
            UsbTmcCodec.WriteRequestDevDepMsgIn(
                new UsbTmcRequestDevDepMsgIn(2, 1024, TermCharEnabled: false, TermChar: 0)
            )
        );
        await client.FlushAsync(bench.Token);

        var first = await client.ReadSubmitReplyAsync(bench.Token);
        var second = await client.ReadSubmitReplyAsync(bench.Token);
        var third = await client.ReadSubmitReplyAsync(bench.Token);

        // The two bulk-OUT transfers are acknowledged in order; the
        // parked IN URB completes only after the transfer that made an
        // answer available.
        first.Reply.Header.SeqNum.ShouldBe(outbound);
        second.Reply.Header.SeqNum.ShouldBe(request);
        third.Reply.Header.SeqNum.ShouldBe(parked);
        third.Reply.Status.ShouldBe(0);

        var message = UsbTmcCodec.ReadDevDepMsgIn(third.Payload);
        Encoding.ASCII.GetString(message.Payload).ShouldBe(IdnResponse + "\n");
    }

    [Fact]
    public async Task A_service_request_completes_an_interrupt_urb_the_host_had_parked()
    {
        await using var bench = await Bench.StartAsync();
        var client = await bench.ImportAsync();

        var interrupt = client.SubmitInterruptIn(UsbTmcConstants.NotificationSize);
        await client.FlushAsync(bench.Token);

        // A round trip on another endpoint proves the interrupt URB
        // reached the device and parked: its reply is not the one the
        // wire carries next.
        await client.ControlInAsync(
            UsbIpTestClient.DeviceToHostStandardDevice,
            UsbStandardRequest.GetDescriptor,
            wValue: UsbDescriptorType.Device << 8,
            wIndex: 0,
            wLength: 64,
            bench.Token
        );

        // Nothing arrives from the host now. The URB completes because
        // the backend raised a service request, which is the whole point
        // of the endpoint.
        bench.Backend.RaiseServiceRequest(bench.Device.Name);

        var notification = await client.ReadSubmitReplyAsync(bench.Token);
        notification.Reply.Header.SeqNum.ShouldBe(interrupt);
        notification.Reply.Status.ShouldBe(0);
        notification.Payload.ShouldBe([
            0x81, // bNotify1: the SRQ notification of USB488 1.00 §3.4.1
            0x40, // bNotify2: the status byte, RQS set
        ]);
    }

    [Fact]
    public async Task A_service_request_raised_before_the_urb_completes_it_on_submit()
    {
        await using var bench = await Bench.StartAsync();
        var client = await bench.ImportAsync();

        bench.Backend.RaiseServiceRequest(bench.Device.Name, statusByte: 0x50);

        var notification = await client.InterruptInAsync(
            UsbTmcConstants.NotificationSize,
            bench.Token
        );

        notification.Reply.Status.ShouldBe(0);
        notification.Payload.ShouldBe([0x81, 0x50]);
    }

    [Fact]
    public async Task A_serial_poll_answers_the_control_transfer_and_the_interrupt_endpoint()
    {
        await using var bench = await Bench.StartAsync();
        var client = await bench.ImportAsync();

        bench.Backend.RaiseServiceRequest(bench.Device.Name);
        var srq = await client.InterruptInAsync(UsbTmcConstants.NotificationSize, bench.Token);
        srq.Payload.ShouldBe([0x81, 0x40]);

        // The host queues the next interrupt URB before polling, the way
        // a driver that never wants to miss a notification does.
        var interrupt = client.SubmitInterruptIn(UsbTmcConstants.NotificationSize);
        var poll = await client.ControlInAsync(
            UsbIpTestClient.DeviceToHostClassInterface,
            UsbTmcConstants.Request488ReadStatusByte,
            wValue: 2,
            wIndex: UsbTmcDeviceProfile.InterfaceNumber,
            wLength: UsbTmcConstants.ReadStatusByteResponseSize,
            bench.Token
        );

        poll.Reply.Status.ShouldBe(0);
        poll.Payload.ShouldBe([
            0x01, // USBTMC_status = SUCCESS
            0x02, // bTag, echoed from wValue
            0x40, // the status byte
        ]);

        var answer = await client.ReadSubmitReplyAsync(bench.Token);
        answer.Reply.Header.SeqNum.ShouldBe(interrupt);
        answer.Payload.ShouldBe([
            0x82, // bNotify1: 0x80 | bTag — a READ_STATUS_BYTE answer
            0x40, // bNotify2: the status byte
        ]);
    }

    [Fact]
    public async Task A_TRIGGER_message_is_acknowledged_and_reaches_the_backend()
    {
        await using var bench = await Bench.StartAsync();
        var client = await bench.ImportAsync();

        var transfer = UsbTmcCodec.WriteTrigger(new UsbTmcTrigger(BTag: 1));
        var triggered = await client.BulkOutAsync(transfer, bench.Token);

        triggered.Reply.Status.ShouldBe(0);
        triggered.Reply.ActualLength.ShouldBe(transfer.Length);

        // The URB is acknowledged only after the trigger reached the
        // backend, so no wait is needed to observe it.
        bench.Backend.TriggerCountFor(bench.Device.Name).ShouldBe(1);
    }

    [Fact]
    public async Task Unlinking_a_parked_interrupt_urb_answers_ECONNRESET_and_never_completes_it()
    {
        await using var bench = await Bench.StartAsync();
        var client = await bench.ImportAsync();

        var interrupt = client.SubmitInterruptIn(bufferLength: 2);
        var unlink = client.SubmitUnlink(interrupt);
        await client.FlushAsync(bench.Token);

        var answer = await client.ReadUnlinkReplyAsync(bench.Token);
        answer.Header.SeqNum.ShouldBe(unlink);
        answer.Status.ShouldBe(UsbIpGatewayServer.UrbUnlinkedStatus);
        answer.Status.ShouldBe(-104);

        // Nothing is owed for the unlinked URB: the next reply on the
        // wire belongs to the request submitted after it.
        var probe = await client.ControlInAsync(
            UsbIpTestClient.DeviceToHostStandardDevice,
            UsbStandardRequest.GetDescriptor,
            wValue: UsbDescriptorType.Device << 8,
            wIndex: 0,
            wLength: 64,
            bench.Token
        );
        probe.Reply.Header.SeqNum.ShouldNotBe(interrupt);
        probe.Payload.Length.ShouldBe(UsbDescriptors.DeviceDescriptorLength);
    }

    [Fact]
    public async Task A_write_is_acknowledged_and_reaches_the_backend()
    {
        await using var bench = await Bench.StartAsync();
        var client = await bench.ImportAsync();

        var transfer = UsbTmcCodec.WriteDevDepMsgOut(
            new UsbTmcDevDepMsgOut(
                BTag: 1,
                EndOfMessage: true,
                Encoding.ASCII.GetBytes(":VOLT 24.000\n")
            )
        );
        var written = await client.BulkOutAsync(transfer, bench.Token);

        written.Reply.Status.ShouldBe(0);
        written.Reply.ActualLength.ShouldBe(transfer.Length);

        var recorded = await bench.Backend.ReadAsync(bench.Device, bench.Token);
        recorded.ShouldBeOk().ShouldBe(":VOLT 24.000");
    }

    /// <summary>
    /// One running gateway over a free loopback port, plus the pieces a
    /// test needs to talk to it.
    /// </summary>
    private sealed class Bench : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly Task _serverTask;
        private readonly int _port;
        private readonly List<UsbIpTestClient> _clients = [];

        private Bench(
            CancellationTokenSource cts,
            Task serverTask,
            int port,
            FakeBackend backend,
            Device device
        )
        {
            _cts = cts;
            _serverTask = serverTask;
            _port = port;
            Backend = backend;
            Device = device;
        }

        public FakeBackend Backend { get; }

        public Device Device { get; }

        public CancellationToken Token => _cts.Token;

        public static async Task<Bench> StartAsync()
        {
            var port = GetFreePort();
            var deviceName = DeviceName.From("dut").ShouldBeOk();
            var device = new Device(
                deviceName,
                VisaResource.Parse("TCPIP0::127.0.0.1::5025::SOCKET").ShouldBeOk(),
                Timeout.FromMilliseconds(3000).ShouldBeOk()
            );
            var serverName = ServerName.From("usb-srv").ShouldBeOk();
            var server = new IviCli.Domain.Servers.Server(
                serverName,
                ServerType.UsbIp,
                IpAddress.From("127.0.0.1").ShouldBeOk(),
                Port.From(port).ShouldBeOk()
            );
            var config = ConfigDocument
                .Empty.AddDevice(device)
                .ShouldBeOk()
                .AddServer(server)
                .ShouldBeOk()
                .AddRoute(
                    new Route(serverName, PublicEndpoint.From(BusId).ShouldBeOk(), deviceName)
                )
                .ShouldBeOk();

            var backend = new FakeBackend().ConfigureDevice(deviceName, IdnResponse);
            var gateway = new UsbIpGatewayServer(
                new FakeBackendFactory(backend),
                NullLogger<UsbIpGatewayServer>.Instance
            );

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var serverTask = gateway.RunAsync(server, config, cts.Token);
            await WaitForListenerAsync(port, cts.Token);
            return new Bench(cts, serverTask, port, backend, device);
        }

        public UsbIpTestClient Connect()
        {
            var client = new UsbIpTestClient(_port);
            _clients.Add(client);
            return client;
        }

        public async Task<UsbIpTestClient> ImportAsync()
        {
            var client = Connect();
            var reply = await client.RequestImportAsync(BusId, Token);
            reply.Status.ShouldBe(UsbIpConstants.StatusOk);
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var client in _clients)
            {
                client.Dispose();
            }

            await _cts.CancelAsync();
            try
            {
                await _serverTask;
            }
            catch (OperationCanceledException) { }
            _cts.Dispose();
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static async Task WaitForListenerAsync(int port, CancellationToken ct)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
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

            throw new TimeoutException($"USB/IP gateway did not start listening on {port}");
        }
    }

    /// <summary>
    /// A usbip client that speaks the wire protocol and nothing else —
    /// the same role <c>usbip attach</c> plus <c>vhci-hcd</c> play, with
    /// the URB stream driven by the test rather than by a host stack.
    /// </summary>
    private sealed class UsbIpTestClient : IDisposable
    {
        public const byte DeviceToHostStandardDevice = 0x80;
        public const byte HostToDeviceStandardDevice = 0x00;
        public const byte DeviceToHostClassInterface = 0xA1;

        private readonly TcpClient _tcp;
        private readonly Dictionary<uint, uint> _directions = [];
        private readonly List<byte> _pending = [];
        private NetworkStream? _stream;
        private uint _seqNum;

        public UsbIpTestClient(int port)
        {
            _tcp = new TcpClient();
            _tcp.Connect(IPAddress.Loopback, port);
        }

        private NetworkStream Stream => _stream ??= _tcp.GetStream();

        public async Task<OpRepDevlist> RequestDevlistAsync(CancellationToken ct)
        {
            var writer = new UsbIpCodec.UsbIpWriter();
            UsbIpCodec.WriteOpReqDevlist(writer, new OpReqDevlist(UsbIpConstants.ProtocolVersion));
            await Stream.WriteAsync(writer.ToArray(), ct);

            // The server closes after answering a devlist, so the whole
            // reply is everything up to end of stream.
            using var buffer = new MemoryStream();
            await Stream.CopyToAsync(buffer, ct);
            var reader = new UsbIpCodec.UsbIpReader(buffer.ToArray());
            return UsbIpCodec.ReadOpRepDevlist(ref reader);
        }

        public async Task<OpRepImport> RequestImportAsync(string busId, CancellationToken ct)
        {
            var writer = new UsbIpCodec.UsbIpWriter();
            UsbIpCodec.WriteOpReqImport(
                writer,
                new OpReqImport(UsbIpConstants.ProtocolVersion, busId)
            );
            await Stream.WriteAsync(writer.ToArray(), ct);

            var preamble = await ReadExactlyAsync(UsbIpConstants.OpHeaderSize, ct);
            var status = (uint)(
                (preamble[4] << 24) | (preamble[5] << 16) | (preamble[6] << 8) | preamble[7]
            );
            var whole =
                status == UsbIpConstants.StatusOk
                    ? [.. preamble, .. await ReadExactlyAsync(UsbIpConstants.DeviceInfoSize, ct)]
                    : preamble;
            var reader = new UsbIpCodec.UsbIpReader(whole);
            return UsbIpCodec.ReadOpRepImport(ref reader);
        }

        public Task<SubmitReply> ControlInAsync(
            byte bmRequestType,
            byte bRequest,
            ushort wValue,
            ushort wIndex,
            ushort wLength,
            CancellationToken ct
        ) =>
            RoundTripAsync(
                Submit(
                    UsbIpConstants.DirIn,
                    endpoint: 0,
                    new UsbSetupPacket(bmRequestType, bRequest, wValue, wIndex, wLength).ToArray(),
                    wLength,
                    []
                ),
                ct
            );

        public Task<SubmitReply> ControlOutAsync(
            byte bmRequestType,
            byte bRequest,
            ushort wValue,
            ushort wIndex,
            CancellationToken ct
        ) =>
            RoundTripAsync(
                Submit(
                    UsbIpConstants.DirOut,
                    endpoint: 0,
                    new UsbSetupPacket(bmRequestType, bRequest, wValue, wIndex, 0).ToArray(),
                    0,
                    []
                ),
                ct
            );

        public Task<SubmitReply> BulkOutAsync(byte[] transfer, CancellationToken ct) =>
            RoundTripAsync(SubmitBulkOut(transfer), ct);

        public Task<SubmitReply> BulkInAsync(int bufferLength, CancellationToken ct) =>
            RoundTripAsync(SubmitBulkIn(bufferLength), ct);

        public Task<SubmitReply> InterruptInAsync(int bufferLength, CancellationToken ct) =>
            RoundTripAsync(SubmitInterruptIn(bufferLength), ct);

        public uint SubmitBulkOut(byte[] transfer) =>
            Submit(UsbIpConstants.DirOut, endpoint: 1, NoSetup, transfer.Length, transfer);

        public uint SubmitBulkIn(int bufferLength) =>
            Submit(UsbIpConstants.DirIn, endpoint: 1, NoSetup, bufferLength, []);

        public uint SubmitInterruptIn(int bufferLength) =>
            Submit(UsbIpConstants.DirIn, endpoint: 2, NoSetup, bufferLength, []);

        public uint SubmitUnlink(uint targetSeqNum)
        {
            var seqNum = ++_seqNum;
            var writer = new UsbIpCodec.UsbIpWriter();
            UsbIpCodec.WriteCmdUnlink(
                writer,
                new UsbIpCmdUnlink(
                    new UsbIpHeaderBasic(UsbIpConstants.CmdUnlink, seqNum, DevId, 0, 0),
                    targetSeqNum
                )
            );
            _pending.AddRange(writer.ToArray());
            return seqNum;
        }

        public async Task FlushAsync(CancellationToken ct)
        {
            var bytes = _pending.ToArray();
            _pending.Clear();
            await Stream.WriteAsync(bytes, ct);
        }

        public async Task<SubmitReply> ReadSubmitReplyAsync(CancellationToken ct)
        {
            var header = await ReadExactlyAsync(UsbIpConstants.CommandHeaderSize, ct);
            var reader = new UsbIpCodec.UsbIpReader(header);
            var reply = UsbIpCodec.ReadRetSubmit(ref reader);
            var length = UsbIpCodec.RetSubmitPayloadLength(_directions[reply.Header.SeqNum], reply);
            var payload = length > 0 ? await ReadExactlyAsync(length, ct) : [];
            return new SubmitReply(reply, payload);
        }

        public async Task<UsbIpRetUnlink> ReadUnlinkReplyAsync(CancellationToken ct)
        {
            var header = await ReadExactlyAsync(UsbIpConstants.CommandHeaderSize, ct);
            var reader = new UsbIpCodec.UsbIpReader(header);
            return UsbIpCodec.ReadRetUnlink(ref reader);
        }

        public void Dispose()
        {
            _stream?.Dispose();
            _tcp.Dispose();
        }

        private async Task<SubmitReply> RoundTripAsync(uint seqNum, CancellationToken ct)
        {
            await FlushAsync(ct);
            var reply = await ReadSubmitReplyAsync(ct);
            reply.Reply.Header.SeqNum.ShouldBe(seqNum);
            return reply;
        }

        private uint Submit(
            uint direction,
            uint endpoint,
            byte[] setup,
            int transferBufferLength,
            byte[] outPayload
        )
        {
            var seqNum = ++_seqNum;
            _directions[seqNum] = direction;
            var writer = new UsbIpCodec.UsbIpWriter();
            UsbIpCodec.WriteCmdSubmit(
                writer,
                new UsbIpCmdSubmit(
                    Header: new UsbIpHeaderBasic(
                        UsbIpConstants.CmdSubmit,
                        seqNum,
                        DevId,
                        direction,
                        endpoint
                    ),
                    TransferFlags: 0,
                    TransferBufferLength: transferBufferLength,
                    StartFrame: 0,
                    NumberOfPackets: UsbIpConstants.NumberOfPacketsNonIso,
                    Interval: 0,
                    Setup: setup
                )
            );
            _pending.AddRange(writer.ToArray());
            _pending.AddRange(outPayload);
            return seqNum;
        }

        private async Task<byte[]> ReadExactlyAsync(int count, CancellationToken ct)
        {
            var buffer = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = await Stream.ReadAsync(buffer.AsMemory(offset), ct);
                if (read == 0)
                {
                    throw new EndOfStreamException(
                        $"gateway closed after {offset} of {count} bytes"
                    );
                }
                offset += read;
            }
            return buffer;
        }

        private static readonly byte[] NoSetup = new byte[UsbIpConstants.SetupSize];

        private const uint DevId = 0x0001_0001;
    }

    private sealed record SubmitReply(UsbIpRetSubmit Reply, byte[] Payload);
}
