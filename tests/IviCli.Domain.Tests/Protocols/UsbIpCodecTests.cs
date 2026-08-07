using System.Buffers.Binary;
using System.Text;
using IviCli.Domain.Protocols;

namespace IviCli.Domain.Tests.Protocols;

/// <summary>
/// Golden-vector tests for the USB/IP wire codec. Every expected buffer
/// here is stamped field-by-field at the absolute offsets published in
/// the kernel's USB/IP protocol document
/// (https://docs.kernel.org/usb/usbip_protocol.html) so a one-byte drift
/// in any layout fails loudly instead of surfacing as a client that
/// silently refuses to attach. Fields are network (big endian) byte
/// order throughout.
/// </summary>
public sealed class UsbIpCodecTests
{
    private const string GoldenPath = "/sys/devices/usbip/1-1";
    private const string GoldenBusId = "1-1";

    /// <summary>
    /// The device block the goldens share: a USBTMC-USB488 instrument on
    /// bus 1, device 2, high speed, one configuration, one interface.
    /// </summary>
    private static UsbIpDeviceInfo GoldenDevice =>
        new(
            Path: GoldenPath,
            BusId: GoldenBusId,
            BusNum: 1,
            DevNum: 2,
            Speed: UsbIpConstants.SpeedHigh,
            IdVendor: 0x1AB1,
            IdProduct: 0x0588,
            BcdDevice: 0x0100,
            DeviceClass: 0x00,
            DeviceSubClass: 0x00,
            DeviceProtocol: 0x00,
            ConfigurationValue: 1,
            NumConfigurations: 1,
            NumInterfaces: 1
        );

    private static UsbIpInterfaceInfo GoldenInterface =>
        new(InterfaceClass: 0xFE, InterfaceSubClass: 0x03, InterfaceProtocol: 0x01);

    [Fact]
    public void ReadOpReqDevlist_parses_the_golden_request()
    {
        // Given the 8-byte OP_REQ_DEVLIST from the kernel doc:
        //   0x00 2  version 0x0111
        //   0x02 2  command code 0x8005
        //   0x04 4  status, unused, shall be 0
        var golden = new byte[] { 0x01, 0x11, 0x80, 0x05, 0x00, 0x00, 0x00, 0x00 };

        // When decoded
        var reader = new UsbIpCodec.UsbIpReader(golden);
        var message = UsbIpCodec.ReadOpReqDevlist(ref reader);

        // Then the version is exposed and the buffer is fully consumed
        message.Version.ShouldBe(UsbIpConstants.ProtocolVersion);
        reader.Remaining.ShouldBe(0);
    }

    [Fact]
    public void WriteOpReqDevlist_round_trips_the_golden_request()
    {
        var golden = new byte[] { 0x01, 0x11, 0x80, 0x05, 0x00, 0x00, 0x00, 0x00 };

        var reader = new UsbIpCodec.UsbIpReader(golden);
        var writer = new UsbIpCodec.UsbIpWriter();
        UsbIpCodec.WriteOpReqDevlist(writer, UsbIpCodec.ReadOpReqDevlist(ref reader));

        writer.ToArray().ShouldBe(golden);
    }

    [Fact]
    public void WriteOpRepDevlist_emits_the_golden_reply_for_one_device_and_one_interface()
    {
        // Given the OP_REP_DEVLIST layout: an 0x0C-byte preamble, then a
        // 0x138-byte device block, then bNumInterfaces * 4 bytes.
        var golden = new byte[0x0C + 0x138 + 4];
        golden[0x00] = 0x01; // version 0x0111
        golden[0x01] = 0x11;
        golden[0x02] = 0x00; // reply code 0x0005
        golden[0x03] = 0x05;
        PutU32(golden, 0x04, 0); // status: 0 for OK
        PutU32(golden, 0x08, 1); // number of exported devices
        StampDeviceBlock(golden, 0x0C);
        golden[0x144] = 0xFE; // bInterfaceClass  (application specific)
        golden[0x145] = 0x03; // bInterfaceSubClass (USBTMC)
        golden[0x146] = 0x01; // bInterfaceProtocol (USB488)
        golden[0x147] = 0x00; // padding byte, shall be zero

        // When the same device is encoded
        var writer = new UsbIpCodec.UsbIpWriter();
        UsbIpCodec.WriteOpRepDevlist(
            writer,
            new OpRepDevlist(
                Version: UsbIpConstants.ProtocolVersion,
                Status: UsbIpConstants.StatusOk,
                Devices: [new UsbIpExportedDevice(GoldenDevice, [GoldenInterface])]
            )
        );

        // Then the bytes match the doc's offsets exactly
        writer.ToArray().ShouldBe(golden);
    }

    [Fact]
    public void ReadOpRepDevlist_round_trips_the_golden_reply()
    {
        var golden = new byte[0x0C + 0x138 + 4];
        golden[0x00] = 0x01;
        golden[0x01] = 0x11;
        golden[0x03] = 0x05;
        PutU32(golden, 0x08, 1);
        StampDeviceBlock(golden, 0x0C);
        golden[0x144] = 0xFE;
        golden[0x145] = 0x03;
        golden[0x146] = 0x01;

        var reader = new UsbIpCodec.UsbIpReader(golden);
        var message = UsbIpCodec.ReadOpRepDevlist(ref reader);
        message.Devices.Length.ShouldBe(1);
        message.Devices[0].Device.ShouldBe(GoldenDevice);
        message.Devices[0].Interfaces.ShouldBe([GoldenInterface]);
        reader.Remaining.ShouldBe(0);

        var writer = new UsbIpCodec.UsbIpWriter();
        UsbIpCodec.WriteOpRepDevlist(writer, message);
        writer.ToArray().ShouldBe(golden);
    }

    [Fact]
    public void ReadOpReqImport_parses_the_golden_request_with_busid_1_1()
    {
        // Given OP_REQ_IMPORT: version, 0x8003, status, then a 32-byte
        // zero-padded busid at offset 8.
        var golden = new byte[0x08 + 0x20];
        golden[0x00] = 0x01;
        golden[0x01] = 0x11;
        golden[0x02] = 0x80; // command code 0x8003
        golden[0x03] = 0x03;
        PutAscii(golden, 0x08, GoldenBusId);

        // When decoded
        var reader = new UsbIpCodec.UsbIpReader(golden);
        var message = UsbIpCodec.ReadOpReqImport(ref reader);

        // Then the busid is the NUL-trimmed string
        message.Version.ShouldBe(UsbIpConstants.ProtocolVersion);
        message.BusId.ShouldBe(GoldenBusId);
        reader.Remaining.ShouldBe(0);
    }

    [Fact]
    public void WriteOpReqImport_round_trips_the_golden_request()
    {
        var golden = new byte[0x08 + 0x20];
        golden[0x00] = 0x01;
        golden[0x01] = 0x11;
        golden[0x02] = 0x80;
        golden[0x03] = 0x03;
        PutAscii(golden, 0x08, GoldenBusId);

        var reader = new UsbIpCodec.UsbIpReader(golden);
        var writer = new UsbIpCodec.UsbIpWriter();
        UsbIpCodec.WriteOpReqImport(writer, UsbIpCodec.ReadOpReqImport(ref reader));

        writer.ToArray().ShouldBe(golden);
    }

    [Fact]
    public void WriteOpRepImport_emits_status_and_device_block_on_success()
    {
        // Given OP_REP_IMPORT with status 0: the 0x138-byte device block
        // starts at offset 8, so busid lands at 0x108 and bNumInterfaces
        // at 0x13F. No interface descriptors are appended.
        var golden = new byte[0x08 + 0x138];
        golden[0x00] = 0x01;
        golden[0x01] = 0x11;
        golden[0x02] = 0x00; // reply code 0x0003
        golden[0x03] = 0x03;
        PutU32(golden, 0x04, 0); // status: 0 for OK
        StampDeviceBlock(golden, 0x08);

        var writer = new UsbIpCodec.UsbIpWriter();
        UsbIpCodec.WriteOpRepImport(
            writer,
            new OpRepImport(
                Version: UsbIpConstants.ProtocolVersion,
                Status: UsbIpConstants.StatusOk,
                Device: GoldenDevice
            )
        );

        writer.ToArray().ShouldBe(golden);
    }

    [Fact]
    public void WriteOpRepImport_ends_at_the_status_field_on_failure()
    {
        // Given status 1: "the reply ends with the status field".
        var golden = new byte[] { 0x01, 0x11, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01 };

        var writer = new UsbIpCodec.UsbIpWriter();
        UsbIpCodec.WriteOpRepImport(
            writer,
            new OpRepImport(
                Version: UsbIpConstants.ProtocolVersion,
                Status: UsbIpConstants.StatusError,
                Device: null
            )
        );

        writer.ToArray().ShouldBe(golden);
    }

    [Fact]
    public void ReadOpRepImport_round_trips_both_goldens()
    {
        var success = new byte[0x08 + 0x138];
        success[0x00] = 0x01;
        success[0x01] = 0x11;
        success[0x03] = 0x03;
        StampDeviceBlock(success, 0x08);

        var reader = new UsbIpCodec.UsbIpReader(success);
        var decoded = UsbIpCodec.ReadOpRepImport(ref reader);
        decoded.Status.ShouldBe(UsbIpConstants.StatusOk);
        decoded.Device.ShouldBe(GoldenDevice);
        var writer = new UsbIpCodec.UsbIpWriter();
        UsbIpCodec.WriteOpRepImport(writer, decoded);
        writer.ToArray().ShouldBe(success);

        var failure = new byte[] { 0x01, 0x11, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01 };
        var failureReader = new UsbIpCodec.UsbIpReader(failure);
        var decodedFailure = UsbIpCodec.ReadOpRepImport(ref failureReader);
        decodedFailure.Status.ShouldBe(UsbIpConstants.StatusError);
        decodedFailure.Device.ShouldBeNull();
        var failureWriter = new UsbIpCodec.UsbIpWriter();
        UsbIpCodec.WriteOpRepImport(failureWriter, decodedFailure);
        failureWriter.ToArray().ShouldBe(failure);
    }

    [Fact]
    public void ReadCmdSubmit_parses_a_control_in_on_endpoint_zero_preserving_setup()
    {
        // Given USBIP_CMD_SUBMIT for GET_DESCRIPTOR(DEVICE):
        //   0x00 20 usbip_header_basic, command 1
        //   0x14 4  transfer_flags
        //   0x18 4  transfer_buffer_length = 18
        //   0x1C 4  start_frame = 0
        //   0x20 4  number_of_packets = 0xffffffff (not ISO)
        //   0x24 4  interval
        //   0x28 8  setup
        var golden = new byte[0x30];
        PutU32(golden, 0x00, 1); // command: USBIP_CMD_SUBMIT
        PutU32(golden, 0x04, 0x0000_0001); // seqnum
        PutU32(golden, 0x08, 0x0001_0002); // devid = (busnum << 16) | devnum
        PutU32(golden, 0x0C, 1); // direction: USBIP_DIR_IN
        PutU32(golden, 0x10, 0); // ep 0
        PutU32(golden, 0x14, 0); // transfer_flags
        PutU32(golden, 0x18, 18); // transfer_buffer_length
        PutU32(golden, 0x1C, 0); // start_frame
        PutU32(golden, 0x20, 0xFFFF_FFFF); // number_of_packets
        PutU32(golden, 0x24, 0); // interval
        golden[0x28] = 0x80; // bmRequestType: device-to-host, standard
        golden[0x29] = 0x06; // bRequest: GET_DESCRIPTOR
        golden[0x2A] = 0x00; // wValue lo
        golden[0x2B] = 0x01; // wValue hi: DEVICE descriptor
        golden[0x2C] = 0x00; // wIndex lo
        golden[0x2D] = 0x00; // wIndex hi
        golden[0x2E] = 0x12; // wLength lo = 18
        golden[0x2F] = 0x00; // wLength hi

        // When decoded
        var reader = new UsbIpCodec.UsbIpReader(golden);
        var message = UsbIpCodec.ReadCmdSubmit(ref reader);

        // Then every header field and the setup bytes survive verbatim
        message.Header.Command.ShouldBe(UsbIpConstants.CmdSubmit);
        message.Header.SeqNum.ShouldBe(1u);
        message.Header.DevId.ShouldBe(0x0001_0002u);
        message.Header.Direction.ShouldBe(UsbIpConstants.DirIn);
        message.Header.Ep.ShouldBe(0u);
        message.TransferBufferLength.ShouldBe(18);
        message.NumberOfPackets.ShouldBe(-1);
        message.Setup.ShouldBe([0x80, 0x06, 0x00, 0x01, 0x00, 0x00, 0x12, 0x00]);
        reader.Remaining.ShouldBe(0);

        var writer = new UsbIpCodec.UsbIpWriter();
        UsbIpCodec.WriteCmdSubmit(writer, message);
        writer.ToArray().ShouldBe(golden);
    }

    [Fact]
    public void ReadCmdSubmit_parses_a_bulk_out_whose_payload_follows_the_header()
    {
        // Given a bulk OUT on ep1 carrying "*IDN?\n": the payload follows
        // the 48-byte header because direction is USBIP_DIR_OUT.
        var payload = Encoding.ASCII.GetBytes("*IDN?\n");
        var golden = new byte[0x30 + 6];
        PutU32(golden, 0x00, 1); // command
        PutU32(golden, 0x04, 2); // seqnum
        PutU32(golden, 0x08, 0x0001_0002); // devid
        PutU32(golden, 0x0C, 0); // direction: USBIP_DIR_OUT
        PutU32(golden, 0x10, 1); // ep 1
        PutU32(golden, 0x18, 6); // transfer_buffer_length
        PutU32(golden, 0x20, 0xFFFF_FFFF); // number_of_packets
        payload.CopyTo(golden.AsSpan(0x30)); // transfer_buffer

        // When the header is decoded and the length rule applied
        var reader = new UsbIpCodec.UsbIpReader(golden);
        var message = UsbIpCodec.ReadCmdSubmit(ref reader);
        var body = reader.ReadBytes(UsbIpCodec.CmdSubmitPayloadLength(message));

        // Then the payload is exactly the transfer_buffer_length bytes
        message.Header.Direction.ShouldBe(UsbIpConstants.DirOut);
        message.Header.Ep.ShouldBe(1u);
        body.ShouldBe(payload);
        reader.Remaining.ShouldBe(0);

        var writer = new UsbIpCodec.UsbIpWriter();
        UsbIpCodec.WriteCmdSubmit(writer, message);
        writer.WriteBytes(body);
        writer.ToArray().ShouldBe(golden);
    }

    [Fact]
    public void WriteRetSubmit_emits_an_in_completion_with_the_payload_appended()
    {
        // Given USBIP_RET_SUBMIT for the IN above:
        //   0x14 4 status = 0
        //   0x18 4 actual_length = 6
        //   0x28 8 padding, shall be 0
        //   0x30 n transfer_buffer (direction IN => n = actual_length)
        var payload = Encoding.ASCII.GetBytes("MOCK\r\n");
        var golden = new byte[0x30 + 6];
        PutU32(golden, 0x00, 3); // command: USBIP_RET_SUBMIT
        PutU32(golden, 0x04, 1); // seqnum echoes the request
        PutU32(golden, 0x08, 0); // devid: server side shall be 0
        PutU32(golden, 0x0C, 0); // direction: server side shall be 0
        PutU32(golden, 0x10, 0); // ep: server side shall be 0
        PutU32(golden, 0x14, 0); // status
        PutU32(golden, 0x18, 6); // actual_length
        PutU32(golden, 0x1C, 0); // start_frame
        PutU32(golden, 0x20, 0xFFFF_FFFF); // number_of_packets
        PutU32(golden, 0x24, 0); // error_count
        payload.CopyTo(golden.AsSpan(0x30));

        var message = new UsbIpRetSubmit(
            Header: new UsbIpHeaderBasic(
                Command: UsbIpConstants.RetSubmit,
                SeqNum: 1,
                DevId: 0,
                Direction: 0,
                Ep: 0
            ),
            Status: 0,
            ActualLength: 6,
            StartFrame: 0,
            NumberOfPackets: -1,
            ErrorCount: 0
        );

        var writer = new UsbIpCodec.UsbIpWriter();
        UsbIpCodec.WriteRetSubmit(writer, message);
        writer.WriteBytes(payload);

        writer.ToArray().ShouldBe(golden);

        var reader = new UsbIpCodec.UsbIpReader(golden);
        UsbIpCodec.ReadRetSubmit(ref reader).ShouldBe(message);
    }

    [Fact]
    public void WriteRetSubmit_emits_a_negative_status_error_completion_without_payload()
    {
        // Given an error completion: status -32 (-EPIPE, a stalled
        // endpoint), actual_length 0, so no transfer_buffer follows.
        var golden = new byte[0x30];
        PutU32(golden, 0x00, 3);
        PutU32(golden, 0x04, 7); // seqnum
        PutU32(golden, 0x14, unchecked((uint)-32)); // status
        PutU32(golden, 0x18, 0); // actual_length
        PutU32(golden, 0x20, 0xFFFF_FFFF); // number_of_packets

        var message = new UsbIpRetSubmit(
            Header: new UsbIpHeaderBasic(UsbIpConstants.RetSubmit, 7, 0, 0, 0),
            Status: -32,
            ActualLength: 0,
            StartFrame: 0,
            NumberOfPackets: -1,
            ErrorCount: 0
        );

        var writer = new UsbIpCodec.UsbIpWriter();
        UsbIpCodec.WriteRetSubmit(writer, message);
        writer.ToArray().ShouldBe(golden);

        var reader = new UsbIpCodec.UsbIpReader(golden);
        UsbIpCodec.ReadRetSubmit(ref reader).Status.ShouldBe(-32);
    }

    [Fact]
    public void ReadCmdUnlink_parses_the_golden_unlink_request()
    {
        // Given USBIP_CMD_UNLINK: header basic (command 2, ep shall be
        // 0), unlink_seqnum at 0x14, then 24 padding bytes.
        var golden = new byte[0x30];
        PutU32(golden, 0x00, 2); // command: USBIP_CMD_UNLINK
        PutU32(golden, 0x04, 3); // seqnum of this unlink request
        PutU32(golden, 0x08, 0x0001_0002); // devid
        PutU32(golden, 0x14, 1); // unlink_seqnum: the CMD_SUBMIT to cancel

        var reader = new UsbIpCodec.UsbIpReader(golden);
        var message = UsbIpCodec.ReadCmdUnlink(ref reader);

        message.Header.Command.ShouldBe(UsbIpConstants.CmdUnlink);
        message.Header.SeqNum.ShouldBe(3u);
        message.Header.Ep.ShouldBe(0u);
        message.UnlinkSeqNum.ShouldBe(1u);
        reader.Remaining.ShouldBe(0);

        var writer = new UsbIpCodec.UsbIpWriter();
        UsbIpCodec.WriteCmdUnlink(writer, message);
        writer.ToArray().ShouldBe(golden);
    }

    [Fact]
    public void WriteRetUnlink_emits_the_golden_unlink_reply()
    {
        // Given USBIP_RET_UNLINK: status -104 (-ECONNRESET, the URB was
        // unlinked), then 24 padding bytes.
        var golden = new byte[0x30];
        PutU32(golden, 0x00, 4); // command: USBIP_RET_UNLINK
        PutU32(golden, 0x04, 3); // seqnum echoes the unlink request
        PutU32(golden, 0x14, unchecked((uint)-104)); // status

        var message = new UsbIpRetUnlink(
            Header: new UsbIpHeaderBasic(UsbIpConstants.RetUnlink, 3, 0, 0, 0),
            Status: -104
        );

        var writer = new UsbIpCodec.UsbIpWriter();
        UsbIpCodec.WriteRetUnlink(writer, message);
        writer.ToArray().ShouldBe(golden);

        var reader = new UsbIpCodec.UsbIpReader(golden);
        UsbIpCodec.ReadRetUnlink(ref reader).ShouldBe(message);
    }

    [Fact]
    public void CmdSubmitPayloadLength_is_transfer_buffer_length_only_for_out()
    {
        // "If direction is USBIP_DIR_OUT then n equals
        //  transfer_buffer_length; otherwise n equals 0."
        var outbound = CmdSubmitWith(UsbIpConstants.DirOut, transferBufferLength: 64);
        var inbound = CmdSubmitWith(UsbIpConstants.DirIn, transferBufferLength: 64);

        UsbIpCodec.CmdSubmitPayloadLength(outbound).ShouldBe(64);
        UsbIpCodec.CmdSubmitPayloadLength(inbound).ShouldBe(0);
    }

    [Fact]
    public void RetSubmitPayloadLength_is_actual_length_only_when_answering_an_in()
    {
        // "If direction is USBIP_DIR_IN then n equals actual_length;
        //  otherwise n equals 0." The reply's own direction field is
        //  zero on the server side, so the rule keys off the request.
        var reply = new UsbIpRetSubmit(
            Header: new UsbIpHeaderBasic(UsbIpConstants.RetSubmit, 1, 0, 0, 0),
            Status: 0,
            ActualLength: 12,
            StartFrame: 0,
            NumberOfPackets: -1,
            ErrorCount: 0
        );

        UsbIpCodec.RetSubmitPayloadLength(UsbIpConstants.DirIn, reply).ShouldBe(12);
        UsbIpCodec.RetSubmitPayloadLength(UsbIpConstants.DirOut, reply).ShouldBe(0);
    }

    [Fact]
    public void Reader_throws_InvalidDataException_on_underrun_exactly_like_the_xdr_reader()
    {
        // Given buffers truncated mid-field, both codecs must fail the
        // same way — the server loop's error handling is shared.
        var truncatedUsbIp = new byte[] { 0x01, 0x11, 0x80, 0x03, 0x00 };
        var truncatedXdr = new byte[] { 0x00, 0x00 };

        Should.Throw<InvalidDataException>(() =>
        {
            var reader = new UsbIpCodec.UsbIpReader(truncatedUsbIp);
            UsbIpCodec.ReadOpReqImport(ref reader);
        });

        Should.Throw<InvalidDataException>(() =>
        {
            var reader = new Vxi11XdrCodec.XdrReader(truncatedXdr);
            reader.ReadUInt32();
        });
    }

    [Fact]
    public void ReadCmdSubmit_throws_InvalidDataException_when_the_header_is_short()
    {
        var truncated = new byte[0x2F];

        Should.Throw<InvalidDataException>(() =>
        {
            var reader = new UsbIpCodec.UsbIpReader(truncated);
            UsbIpCodec.ReadCmdSubmit(ref reader);
        });
    }

    [Fact]
    public void WritePaddedString_zero_fills_a_busid_shorter_than_the_field()
    {
        var writer = new UsbIpCodec.UsbIpWriter();
        writer.WritePaddedString(GoldenBusId, UsbIpConstants.BusIdSize);
        var bytes = writer.ToArray();

        bytes.Length.ShouldBe(32);
        bytes[0].ShouldBe((byte)'1');
        bytes[1].ShouldBe((byte)'-');
        bytes[2].ShouldBe((byte)'1');
        bytes[3..].ShouldAllBe(b => b == 0);

        var reader = new UsbIpCodec.UsbIpReader(bytes);
        reader.ReadPaddedString(UsbIpConstants.BusIdSize).ShouldBe(GoldenBusId);
        reader.Remaining.ShouldBe(0);
    }

    [Fact]
    public void ReadPaddedString_accepts_a_field_with_no_room_for_a_terminator()
    {
        var full = new string('a', UsbIpConstants.BusIdSize);
        var bytes = Encoding.ASCII.GetBytes(full);

        var reader = new UsbIpCodec.UsbIpReader(bytes);
        reader.ReadPaddedString(UsbIpConstants.BusIdSize).ShouldBe(full);
        reader.Remaining.ShouldBe(0);
    }

    [Fact]
    public void WritePaddedString_rejects_a_value_that_leaves_no_room_for_the_terminator()
    {
        var writer = new UsbIpCodec.UsbIpWriter();

        Should.Throw<ArgumentException>(() =>
            writer.WritePaddedString(
                new string('a', UsbIpConstants.BusIdSize),
                UsbIpConstants.BusIdSize
            )
        );
    }

    [Fact]
    public void ReadOpReqImport_rejects_a_foreign_op_code()
    {
        // 0x8005 is OP_REQ_DEVLIST, not OP_REQ_IMPORT.
        var golden = new byte[0x08 + 0x20];
        golden[0x00] = 0x01;
        golden[0x01] = 0x11;
        golden[0x02] = 0x80;
        golden[0x03] = 0x05;

        Should.Throw<InvalidDataException>(() =>
        {
            var reader = new UsbIpCodec.UsbIpReader(golden);
            UsbIpCodec.ReadOpReqImport(ref reader);
        });
    }

    [Fact]
    public void WriteCmdSubmit_rejects_a_setup_field_that_is_not_eight_bytes()
    {
        var message = CmdSubmitWith(UsbIpConstants.DirIn, transferBufferLength: 0) with
        {
            Setup = new byte[7],
        };

        Should.Throw<ArgumentException>(() =>
            UsbIpCodec.WriteCmdSubmit(new UsbIpCodec.UsbIpWriter(), message)
        );
    }

    private static UsbIpCmdSubmit CmdSubmitWith(uint direction, int transferBufferLength) =>
        new(
            Header: new UsbIpHeaderBasic(UsbIpConstants.CmdSubmit, 1, 0x0001_0002, direction, 1),
            TransferFlags: 0,
            TransferBufferLength: transferBufferLength,
            StartFrame: 0,
            NumberOfPackets: -1,
            Interval: 0,
            Setup: new byte[8]
        );

    /// <summary>
    /// Stamps the 0x138-byte device block at <paramref name="offset"/>
    /// using the offsets the kernel doc gives relative to the block's
    /// start (path 0, busid 0x100, busnum 0x120 … bNumInterfaces 0x137).
    /// </summary>
    private static void StampDeviceBlock(byte[] buffer, int offset)
    {
        PutAscii(buffer, offset + 0x000, GoldenPath); // path, 256 bytes
        PutAscii(buffer, offset + 0x100, GoldenBusId); // busid, 32 bytes
        PutU32(buffer, offset + 0x120, 1); // busnum
        PutU32(buffer, offset + 0x124, 2); // devnum
        PutU32(buffer, offset + 0x128, 3); // speed: USB_SPEED_HIGH
        PutU16(buffer, offset + 0x12C, 0x1AB1); // idVendor
        PutU16(buffer, offset + 0x12E, 0x0588); // idProduct
        PutU16(buffer, offset + 0x130, 0x0100); // bcdDevice
        buffer[offset + 0x132] = 0x00; // bDeviceClass
        buffer[offset + 0x133] = 0x00; // bDeviceSubClass
        buffer[offset + 0x134] = 0x00; // bDeviceProtocol
        buffer[offset + 0x135] = 0x01; // bConfigurationValue
        buffer[offset + 0x136] = 0x01; // bNumConfigurations
        buffer[offset + 0x137] = 0x01; // bNumInterfaces
    }

    private static void PutU16(byte[] buffer, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset, 2), value);

    private static void PutU32(byte[] buffer, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset, 4), value);

    private static void PutAscii(byte[] buffer, int offset, string value) =>
        Encoding.ASCII.GetBytes(value).CopyTo(buffer.AsSpan(offset));
}
