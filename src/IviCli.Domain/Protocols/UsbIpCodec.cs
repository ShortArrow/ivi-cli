using System.Buffers.Binary;
using System.Text;

namespace IviCli.Domain.Protocols;

/// <summary>
/// Pure USB/IP wire codec (https://docs.kernel.org/usb/usbip_protocol.html).
/// Reader/Writer types maintain a cursor over a backing byte buffer;
/// every integer is fixed-width big endian, and <c>path</c> / <c>busid</c>
/// are fixed-width NUL-terminated, zero-filled ASCII fields. TCP framing
/// lives outside this type so the Domain layer stays free of
/// <see cref="System.IO.Stream"/> dependencies — the boundary
/// <see cref="Vxi11XdrCodec"/> draws for VXI-11.
///
/// Command messages carry their transfer buffer after the 48-byte
/// header. The header codecs never touch it: the caller appends or
/// consumes exactly the bytes <see cref="CmdSubmitPayloadLength"/> and
/// <see cref="RetSubmitPayloadLength"/> report.
/// </summary>
public static class UsbIpCodec
{
    /// <summary>
    /// Cursor-based big-endian reader. Backed by
    /// <see cref="ReadOnlyMemory{T}"/> rather than a span so instances
    /// can flow through async method boundaries, and a copy of the
    /// struct can probe ahead without consuming the original.
    /// </summary>
    public struct UsbIpReader
    {
        private readonly ReadOnlyMemory<byte> _buffer;
        private int _offset;

        /// <summary>Wraps <paramref name="buffer"/> for sequential reads.</summary>
        public UsbIpReader(ReadOnlyMemory<byte> buffer)
        {
            _buffer = buffer;
            _offset = 0;
        }

        /// <summary>Current cursor position in bytes.</summary>
        public readonly int Position => _offset;

        /// <summary>Bytes remaining after the cursor.</summary>
        public readonly int Remaining => _buffer.Length - _offset;

        /// <summary>Reads one byte.</summary>
        public byte ReadByte()
        {
            EnsureAvailable(1);
            var value = _buffer.Span[_offset];
            _offset += 1;
            return value;
        }

        /// <summary>Reads a 2-byte big-endian unsigned integer.</summary>
        public ushort ReadUInt16()
        {
            EnsureAvailable(2);
            var value = BinaryPrimitives.ReadUInt16BigEndian(_buffer.Span.Slice(_offset, 2));
            _offset += 2;
            return value;
        }

        /// <summary>Reads a 4-byte big-endian unsigned integer.</summary>
        public uint ReadUInt32()
        {
            EnsureAvailable(4);
            var value = BinaryPrimitives.ReadUInt32BigEndian(_buffer.Span.Slice(_offset, 4));
            _offset += 4;
            return value;
        }

        /// <summary>Reads a 4-byte big-endian signed integer.</summary>
        public int ReadInt32()
        {
            return unchecked((int)ReadUInt32());
        }

        /// <summary>Reads <paramref name="count"/> raw bytes.</summary>
        public byte[] ReadBytes(int count)
        {
            EnsureAvailable(count);
            var bytes = _buffer.Span.Slice(_offset, count).ToArray();
            _offset += count;
            return bytes;
        }

        /// <summary>
        /// Reads a fixed-width ASCII field of <paramref name="fieldLength"/>
        /// bytes, returning the text before the first NUL. A field with no
        /// NUL at all yields all <paramref name="fieldLength"/> characters;
        /// the cursor advances by the full field either way.
        /// </summary>
        public string ReadPaddedString(int fieldLength)
        {
            EnsureAvailable(fieldLength);
            var field = _buffer.Span.Slice(_offset, fieldLength);
            var terminator = field.IndexOf((byte)0);
            var length = terminator < 0 ? fieldLength : terminator;
            var value = Encoding.ASCII.GetString(field[..length]);
            _offset += fieldLength;
            return value;
        }

        /// <summary>Skips <paramref name="count"/> bytes of fixed zero padding.</summary>
        public void SkipPadding(int count)
        {
            EnsureAvailable(count);
            _offset += count;
        }

        private readonly void EnsureAvailable(int count)
        {
            if (_offset + count > _buffer.Length)
            {
                throw new InvalidDataException(
                    $"USB/IP read past end of buffer (needed {count}, have {_buffer.Length - _offset})"
                );
            }
        }
    }

    /// <summary>
    /// Growable big-endian writer. Backed by a <see cref="List{T}"/> of
    /// bytes so a message can be assembled without pre-computing its
    /// total size.
    /// </summary>
    public sealed class UsbIpWriter
    {
        private readonly List<byte> _buffer = new();

        /// <summary>The bytes written so far.</summary>
        public byte[] ToArray() => _buffer.ToArray();

        /// <summary>Number of bytes written.</summary>
        public int Length => _buffer.Count;

        /// <summary>Writes one byte.</summary>
        public void WriteByte(byte value) => _buffer.Add(value);

        /// <summary>Writes a 2-byte big-endian unsigned integer.</summary>
        public void WriteUInt16(ushort value)
        {
            Span<byte> tmp = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(tmp, value);
            _buffer.Add(tmp[0]);
            _buffer.Add(tmp[1]);
        }

        /// <summary>Writes a 4-byte big-endian unsigned integer.</summary>
        public void WriteUInt32(uint value)
        {
            Span<byte> tmp = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(tmp, value);
            _buffer.Add(tmp[0]);
            _buffer.Add(tmp[1]);
            _buffer.Add(tmp[2]);
            _buffer.Add(tmp[3]);
        }

        /// <summary>Writes a 4-byte big-endian signed integer.</summary>
        public void WriteInt32(int value) => WriteUInt32(unchecked((uint)value));

        /// <summary>Appends raw bytes verbatim — used for transfer buffers.</summary>
        public void WriteBytes(ReadOnlySpan<byte> bytes)
        {
            for (var i = 0; i < bytes.Length; i++)
            {
                _buffer.Add(bytes[i]);
            }
        }

        /// <summary>Writes <paramref name="count"/> zero bytes.</summary>
        public void WritePadding(int count)
        {
            for (var i = 0; i < count; i++)
            {
                _buffer.Add(0);
            }
        }

        /// <summary>
        /// Writes an ASCII string into a fixed-width field of
        /// <paramref name="fieldLength"/> bytes, zero-filling the rest.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// <paramref name="value"/> leaves no room for the terminating
        /// zero byte the protocol requires.
        /// </exception>
        public void WritePaddedString(string value, int fieldLength)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            if (bytes.Length >= fieldLength)
            {
                throw new ArgumentException(
                    $"Value of {bytes.Length} bytes leaves no room for the terminating zero in a {fieldLength}-byte field",
                    nameof(value)
                );
            }
            WriteBytes(bytes);
            WritePadding(fieldLength - bytes.Length);
        }
    }

    /// <summary>Decodes OP_REQ_DEVLIST.</summary>
    public static OpReqDevlist ReadOpReqDevlist(ref UsbIpReader reader)
    {
        var version = ReadOpPreamble(ref reader, UsbIpConstants.OpReqDevlist);
        return new OpReqDevlist(Version: version);
    }

    /// <summary>Encodes OP_REQ_DEVLIST.</summary>
    public static void WriteOpReqDevlist(UsbIpWriter writer, OpReqDevlist message)
    {
        WriteOpPreamble(
            writer,
            message.Version,
            UsbIpConstants.OpReqDevlist,
            UsbIpConstants.StatusOk
        );
    }

    /// <summary>Decodes OP_REP_DEVLIST, including every device's interface tuples.</summary>
    public static OpRepDevlist ReadOpRepDevlist(ref UsbIpReader reader)
    {
        var version = reader.ReadUInt16();
        EnsureCode(reader.ReadUInt16(), UsbIpConstants.OpRepDevlist);
        var status = reader.ReadUInt32();
        var count = reader.ReadUInt32();

        var devices = new UsbIpExportedDevice[count];
        for (var i = 0; i < devices.Length; i++)
        {
            var device = ReadDeviceInfo(ref reader);
            var interfaces = new UsbIpInterfaceInfo[device.NumInterfaces];
            for (var j = 0; j < interfaces.Length; j++)
            {
                interfaces[j] = ReadInterfaceInfo(ref reader);
            }
            devices[i] = new UsbIpExportedDevice(device, interfaces);
        }

        return new OpRepDevlist(Version: version, Status: status, Devices: devices);
    }

    /// <summary>Encodes OP_REP_DEVLIST.</summary>
    public static void WriteOpRepDevlist(UsbIpWriter writer, OpRepDevlist message)
    {
        WriteOpPreamble(writer, message.Version, UsbIpConstants.OpRepDevlist, message.Status);
        writer.WriteUInt32((uint)message.Devices.Length);
        foreach (var exported in message.Devices)
        {
            WriteDeviceInfo(writer, exported.Device);
            foreach (var descriptor in exported.Interfaces)
            {
                WriteInterfaceInfo(writer, descriptor);
            }
        }
    }

    /// <summary>Decodes OP_REQ_IMPORT.</summary>
    public static OpReqImport ReadOpReqImport(ref UsbIpReader reader)
    {
        var version = ReadOpPreamble(ref reader, UsbIpConstants.OpReqImport);
        return new OpReqImport(
            Version: version,
            BusId: reader.ReadPaddedString(UsbIpConstants.BusIdSize)
        );
    }

    /// <summary>Encodes OP_REQ_IMPORT.</summary>
    public static void WriteOpReqImport(UsbIpWriter writer, OpReqImport message)
    {
        WriteOpPreamble(
            writer,
            message.Version,
            UsbIpConstants.OpReqImport,
            UsbIpConstants.StatusOk
        );
        writer.WritePaddedString(message.BusId, UsbIpConstants.BusIdSize);
    }

    /// <summary>
    /// Decodes OP_REP_IMPORT. The device block is read only when the
    /// status field is <see cref="UsbIpConstants.StatusOk"/>.
    /// </summary>
    public static OpRepImport ReadOpRepImport(ref UsbIpReader reader)
    {
        var version = reader.ReadUInt16();
        EnsureCode(reader.ReadUInt16(), UsbIpConstants.OpRepImport);
        var status = reader.ReadUInt32();
        var device =
            status == UsbIpConstants.StatusOk ? ReadDeviceInfo(ref reader) : (UsbIpDeviceInfo?)null;
        return new OpRepImport(Version: version, Status: status, Device: device);
    }

    /// <summary>Encodes OP_REP_IMPORT, ending at the status field on failure.</summary>
    public static void WriteOpRepImport(UsbIpWriter writer, OpRepImport message)
    {
        WriteOpPreamble(writer, message.Version, UsbIpConstants.OpRepImport, message.Status);
        if (message.Device is { } device)
        {
            WriteDeviceInfo(writer, device);
        }
    }

    /// <summary>Decodes <c>usbip_header_basic</c>.</summary>
    public static UsbIpHeaderBasic ReadHeaderBasic(ref UsbIpReader reader) =>
        new(
            Command: reader.ReadUInt32(),
            SeqNum: reader.ReadUInt32(),
            DevId: reader.ReadUInt32(),
            Direction: reader.ReadUInt32(),
            Ep: reader.ReadUInt32()
        );

    /// <summary>Encodes <c>usbip_header_basic</c>.</summary>
    public static void WriteHeaderBasic(UsbIpWriter writer, UsbIpHeaderBasic header)
    {
        writer.WriteUInt32(header.Command);
        writer.WriteUInt32(header.SeqNum);
        writer.WriteUInt32(header.DevId);
        writer.WriteUInt32(header.Direction);
        writer.WriteUInt32(header.Ep);
    }

    /// <summary>Decodes the 48-byte USBIP_CMD_SUBMIT header.</summary>
    public static UsbIpCmdSubmit ReadCmdSubmit(ref UsbIpReader reader)
    {
        var header = ReadHeaderBasic(ref reader);
        EnsureCommand(header.Command, UsbIpConstants.CmdSubmit);
        return new UsbIpCmdSubmit(
            Header: header,
            TransferFlags: reader.ReadUInt32(),
            TransferBufferLength: reader.ReadInt32(),
            StartFrame: reader.ReadInt32(),
            NumberOfPackets: reader.ReadInt32(),
            Interval: reader.ReadInt32(),
            Setup: reader.ReadBytes(UsbIpConstants.SetupSize)
        );
    }

    /// <summary>Encodes the 48-byte USBIP_CMD_SUBMIT header.</summary>
    /// <exception cref="ArgumentException">
    /// The setup field is not exactly <see cref="UsbIpConstants.SetupSize"/>
    /// bytes, which would shift every following byte on the wire.
    /// </exception>
    public static void WriteCmdSubmit(UsbIpWriter writer, UsbIpCmdSubmit message)
    {
        if (message.Setup.Length != UsbIpConstants.SetupSize)
        {
            throw new ArgumentException(
                $"Setup field must be exactly {UsbIpConstants.SetupSize} bytes, was {message.Setup.Length}",
                nameof(message)
            );
        }
        WriteHeaderBasic(writer, message.Header);
        writer.WriteUInt32(message.TransferFlags);
        writer.WriteInt32(message.TransferBufferLength);
        writer.WriteInt32(message.StartFrame);
        writer.WriteInt32(message.NumberOfPackets);
        writer.WriteInt32(message.Interval);
        writer.WriteBytes(message.Setup);
    }

    /// <summary>Decodes the 48-byte USBIP_RET_SUBMIT header.</summary>
    public static UsbIpRetSubmit ReadRetSubmit(ref UsbIpReader reader)
    {
        var header = ReadHeaderBasic(ref reader);
        EnsureCommand(header.Command, UsbIpConstants.RetSubmit);
        var message = new UsbIpRetSubmit(
            Header: header,
            Status: reader.ReadInt32(),
            ActualLength: reader.ReadInt32(),
            StartFrame: reader.ReadInt32(),
            NumberOfPackets: reader.ReadInt32(),
            ErrorCount: reader.ReadInt32()
        );
        reader.SkipPadding(RetSubmitPaddingSize);
        return message;
    }

    /// <summary>Encodes the 48-byte USBIP_RET_SUBMIT header.</summary>
    public static void WriteRetSubmit(UsbIpWriter writer, UsbIpRetSubmit message)
    {
        WriteHeaderBasic(writer, message.Header);
        writer.WriteInt32(message.Status);
        writer.WriteInt32(message.ActualLength);
        writer.WriteInt32(message.StartFrame);
        writer.WriteInt32(message.NumberOfPackets);
        writer.WriteInt32(message.ErrorCount);
        writer.WritePadding(RetSubmitPaddingSize);
    }

    /// <summary>Decodes the 48-byte USBIP_CMD_UNLINK message.</summary>
    public static UsbIpCmdUnlink ReadCmdUnlink(ref UsbIpReader reader)
    {
        var header = ReadHeaderBasic(ref reader);
        EnsureCommand(header.Command, UsbIpConstants.CmdUnlink);
        var message = new UsbIpCmdUnlink(Header: header, UnlinkSeqNum: reader.ReadUInt32());
        reader.SkipPadding(UnlinkPaddingSize);
        return message;
    }

    /// <summary>Encodes the 48-byte USBIP_CMD_UNLINK message.</summary>
    public static void WriteCmdUnlink(UsbIpWriter writer, UsbIpCmdUnlink message)
    {
        WriteHeaderBasic(writer, message.Header);
        writer.WriteUInt32(message.UnlinkSeqNum);
        writer.WritePadding(UnlinkPaddingSize);
    }

    /// <summary>Decodes the 48-byte USBIP_RET_UNLINK message.</summary>
    public static UsbIpRetUnlink ReadRetUnlink(ref UsbIpReader reader)
    {
        var header = ReadHeaderBasic(ref reader);
        EnsureCommand(header.Command, UsbIpConstants.RetUnlink);
        var message = new UsbIpRetUnlink(Header: header, Status: reader.ReadInt32());
        reader.SkipPadding(UnlinkPaddingSize);
        return message;
    }

    /// <summary>Encodes the 48-byte USBIP_RET_UNLINK message.</summary>
    public static void WriteRetUnlink(UsbIpWriter writer, UsbIpRetUnlink message)
    {
        WriteHeaderBasic(writer, message.Header);
        writer.WriteInt32(message.Status);
        writer.WritePadding(UnlinkPaddingSize);
    }

    /// <summary>
    /// Bytes of transfer buffer that follow a USBIP_CMD_SUBMIT header:
    /// <c>transfer_buffer_length</c> when the direction is
    /// USBIP_DIR_OUT, zero otherwise.
    /// </summary>
    public static int CmdSubmitPayloadLength(UsbIpCmdSubmit message) =>
        message.Header.Direction == UsbIpConstants.DirOut ? message.TransferBufferLength : 0;

    /// <summary>
    /// Bytes of transfer buffer that follow a USBIP_RET_SUBMIT header:
    /// <c>actual_length</c> when the request being answered was
    /// USBIP_DIR_IN, zero otherwise. The direction comes from the
    /// request because a reply's own direction field is fixed at zero.
    /// </summary>
    public static int RetSubmitPayloadLength(uint requestDirection, UsbIpRetSubmit message) =>
        requestDirection == UsbIpConstants.DirIn ? message.ActualLength : 0;

    /// <summary>Decodes the 0x138-byte device block.</summary>
    public static UsbIpDeviceInfo ReadDeviceInfo(ref UsbIpReader reader) =>
        new(
            Path: reader.ReadPaddedString(UsbIpConstants.PathSize),
            BusId: reader.ReadPaddedString(UsbIpConstants.BusIdSize),
            BusNum: reader.ReadUInt32(),
            DevNum: reader.ReadUInt32(),
            Speed: reader.ReadUInt32(),
            IdVendor: reader.ReadUInt16(),
            IdProduct: reader.ReadUInt16(),
            BcdDevice: reader.ReadUInt16(),
            DeviceClass: reader.ReadByte(),
            DeviceSubClass: reader.ReadByte(),
            DeviceProtocol: reader.ReadByte(),
            ConfigurationValue: reader.ReadByte(),
            NumConfigurations: reader.ReadByte(),
            NumInterfaces: reader.ReadByte()
        );

    /// <summary>Encodes the 0x138-byte device block.</summary>
    public static void WriteDeviceInfo(UsbIpWriter writer, UsbIpDeviceInfo device)
    {
        writer.WritePaddedString(device.Path, UsbIpConstants.PathSize);
        writer.WritePaddedString(device.BusId, UsbIpConstants.BusIdSize);
        writer.WriteUInt32(device.BusNum);
        writer.WriteUInt32(device.DevNum);
        writer.WriteUInt32(device.Speed);
        writer.WriteUInt16(device.IdVendor);
        writer.WriteUInt16(device.IdProduct);
        writer.WriteUInt16(device.BcdDevice);
        writer.WriteByte(device.DeviceClass);
        writer.WriteByte(device.DeviceSubClass);
        writer.WriteByte(device.DeviceProtocol);
        writer.WriteByte(device.ConfigurationValue);
        writer.WriteByte(device.NumConfigurations);
        writer.WriteByte(device.NumInterfaces);
    }

    /// <summary>Decodes one interface tuple, consuming its alignment pad.</summary>
    public static UsbIpInterfaceInfo ReadInterfaceInfo(ref UsbIpReader reader)
    {
        var descriptor = new UsbIpInterfaceInfo(
            InterfaceClass: reader.ReadByte(),
            InterfaceSubClass: reader.ReadByte(),
            InterfaceProtocol: reader.ReadByte()
        );
        reader.SkipPadding(1);
        return descriptor;
    }

    /// <summary>Encodes one interface tuple, including its zero alignment pad.</summary>
    public static void WriteInterfaceInfo(UsbIpWriter writer, UsbIpInterfaceInfo descriptor)
    {
        writer.WriteByte(descriptor.InterfaceClass);
        writer.WriteByte(descriptor.InterfaceSubClass);
        writer.WriteByte(descriptor.InterfaceProtocol);
        writer.WritePadding(1);
    }

    private const int RetSubmitPaddingSize = 8;

    private const int UnlinkPaddingSize = 24;

    private static ushort ReadOpPreamble(ref UsbIpReader reader, ushort expectedCode)
    {
        var version = reader.ReadUInt16();
        EnsureCode(reader.ReadUInt16(), expectedCode);
        reader.SkipPadding(4);
        return version;
    }

    private static void WriteOpPreamble(
        UsbIpWriter writer,
        ushort version,
        ushort code,
        uint status
    )
    {
        writer.WriteUInt16(version);
        writer.WriteUInt16(code);
        writer.WriteUInt32(status);
    }

    private static void EnsureCode(ushort actual, ushort expected)
    {
        if (actual != expected)
        {
            throw new InvalidDataException(
                $"USB/IP op code mismatch (expected 0x{expected:X4}, got 0x{actual:X4})"
            );
        }
    }

    private static void EnsureCommand(uint actual, uint expected)
    {
        if (actual != expected)
        {
            throw new InvalidDataException(
                $"USB/IP command mismatch (expected 0x{expected:X8}, got 0x{actual:X8})"
            );
        }
    }
}
