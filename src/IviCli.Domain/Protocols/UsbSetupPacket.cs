using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace IviCli.Domain.Protocols;

/// <summary>Direction bit (D7) of <c>bmRequestType</c>, USB 2.0 §9.3.1.</summary>
public enum UsbTransferDirection
{
    /// <summary>OUT: the data stage, if any, flows host to device.</summary>
    HostToDevice = 0,

    /// <summary>IN: the data stage, if any, flows device to host.</summary>
    DeviceToHost = 1,
}

/// <summary>Type field (D6..D5) of <c>bmRequestType</c>, USB 2.0 §9.3.1.</summary>
public enum UsbRequestType
{
    /// <summary>A request USB 2.0 §9.4 defines for every device.</summary>
    Standard = 0,

    /// <summary>A request the interface's class defines — USBTMC/USB488 here.</summary>
    Class = 1,

    /// <summary>A request the vendor defines.</summary>
    Vendor = 2,

    /// <summary>Reserved by the specification.</summary>
    Reserved = 3,
}

/// <summary>Recipient field (D4..D0) of <c>bmRequestType</c>, USB 2.0 §9.3.1.</summary>
public enum UsbRecipient
{
    /// <summary>The device as a whole.</summary>
    Device = 0,

    /// <summary>One interface, named by <c>wIndex</c>.</summary>
    Interface = 1,

    /// <summary>One endpoint, named by <c>wIndex</c>.</summary>
    Endpoint = 2,

    /// <summary>Anything else the specification leaves open.</summary>
    Other = 3,
}

/// <summary>
/// <c>bRequest</c> values of the standard device requests, USB 2.0
/// table 9-4. Byte-typed rather than an enum because class layers add
/// their own codes to the same field.
/// </summary>
public static class UsbStandardRequest
{
    /// <summary>GET_STATUS: two status bytes for the named recipient.</summary>
    public const byte GetStatus = 0;

    /// <summary>CLEAR_FEATURE.</summary>
    public const byte ClearFeature = 1;

    /// <summary>SET_FEATURE.</summary>
    public const byte SetFeature = 3;

    /// <summary>SET_ADDRESS: assign the bus address during enumeration.</summary>
    public const byte SetAddress = 5;

    /// <summary>GET_DESCRIPTOR: <c>wValue</c> carries type and index.</summary>
    public const byte GetDescriptor = 6;

    /// <summary>SET_DESCRIPTOR.</summary>
    public const byte SetDescriptor = 7;

    /// <summary>GET_CONFIGURATION: the current <c>bConfigurationValue</c>.</summary>
    public const byte GetConfiguration = 8;

    /// <summary>SET_CONFIGURATION: select a configuration, or 0 to unconfigure.</summary>
    public const byte SetConfiguration = 9;

    /// <summary>GET_INTERFACE: the current alternate setting.</summary>
    public const byte GetInterface = 10;

    /// <summary>SET_INTERFACE: select an alternate setting.</summary>
    public const byte SetInterface = 11;

    /// <summary>SYNCH_FRAME.</summary>
    public const byte SynchFrame = 12;
}

/// <summary>
/// <c>bDescriptorType</c> values of the standard descriptors, USB 2.0
/// table 9-5. The same values appear in the high byte of <c>wValue</c>
/// on a GET_DESCRIPTOR request.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "STRING is the specification's own name for descriptor type 3 (USB 2.0 table 9-5); renaming it would break the mapping these constants exist to preserve."
)]
public static class UsbDescriptorType
{
    /// <summary>DEVICE descriptor.</summary>
    public const byte Device = 1;

    /// <summary>CONFIGURATION descriptor, and the hierarchy under it.</summary>
    public const byte Configuration = 2;

    /// <summary>STRING descriptor.</summary>
    public const byte String = 3;

    /// <summary>INTERFACE descriptor — returned inside a configuration only.</summary>
    public const byte Interface = 4;

    /// <summary>ENDPOINT descriptor — returned inside a configuration only.</summary>
    public const byte Endpoint = 5;
}

/// <summary>
/// The 8-byte SETUP packet that opens every control transfer, USB 2.0
/// §9.3. <see cref="WValue"/>, <see cref="WIndex"/> and
/// <see cref="WLength"/> are <strong>little endian</strong> on the wire:
/// USB fixes that byte order for every multi-byte field it defines,
/// while the USB/IP header carrying this packet is big endian
/// (<see cref="UsbIpCodec"/>). The two codecs stay separate for exactly
/// that reason.
/// </summary>
public readonly record struct UsbSetupPacket(
    byte BmRequestType,
    byte BRequest,
    ushort WValue,
    ushort WIndex,
    ushort WLength
)
{
    /// <summary>Size of the SETUP packet on the wire.</summary>
    public const int Size = 8;

    /// <summary>Direction of the data stage, from <c>bmRequestType</c> D7.</summary>
    public UsbTransferDirection Direction =>
        (UsbTransferDirection)((BmRequestType & DirectionMask) >> DirectionShift);

    /// <summary>Who defines this request, from <c>bmRequestType</c> D6..D5.</summary>
    public UsbRequestType Type => (UsbRequestType)((BmRequestType & TypeMask) >> TypeShift);

    /// <summary>What the request addresses, from <c>bmRequestType</c> D4..D0.</summary>
    public UsbRecipient Recipient => (UsbRecipient)(BmRequestType & RecipientMask);

    /// <summary>
    /// High byte of <see cref="WValue"/> — the descriptor type on a
    /// GET_DESCRIPTOR request, meaningless on any other.
    /// </summary>
    public byte DescriptorType => (byte)(WValue >> 8);

    /// <summary>
    /// Low byte of <see cref="WValue"/> — the descriptor index on a
    /// GET_DESCRIPTOR request, meaningless on any other.
    /// </summary>
    public byte DescriptorIndex => (byte)(WValue & 0xFF);

    /// <summary>Decodes the 8 SETUP bytes, little endian.</summary>
    /// <exception cref="InvalidDataException">
    /// <paramref name="setup"/> is not exactly <see cref="Size"/> bytes,
    /// so no field can be located reliably.
    /// </exception>
    public static UsbSetupPacket Read(ReadOnlySpan<byte> setup)
    {
        if (setup.Length != Size)
        {
            throw new InvalidDataException(
                $"SETUP packet must be exactly {Size} bytes, was {setup.Length}"
            );
        }

        return new UsbSetupPacket(
            BmRequestType: setup[0],
            BRequest: setup[1],
            WValue: BinaryPrimitives.ReadUInt16LittleEndian(setup.Slice(2, 2)),
            WIndex: BinaryPrimitives.ReadUInt16LittleEndian(setup.Slice(4, 2)),
            WLength: BinaryPrimitives.ReadUInt16LittleEndian(setup.Slice(6, 2))
        );
    }

    /// <summary>Encodes the 8 SETUP bytes, little endian.</summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="destination"/> is shorter than <see cref="Size"/>.
    /// </exception>
    public void Write(Span<byte> destination)
    {
        if (destination.Length < Size)
        {
            throw new ArgumentException(
                $"SETUP packet needs {Size} bytes, destination holds {destination.Length}",
                nameof(destination)
            );
        }

        destination[0] = BmRequestType;
        destination[1] = BRequest;
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(2, 2), WValue);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4, 2), WIndex);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(6, 2), WLength);
    }

    /// <summary>Encodes the packet into a fresh <see cref="Size"/>-byte array.</summary>
    public byte[] ToArray()
    {
        var bytes = new byte[Size];
        Write(bytes);
        return bytes;
    }

    private const byte DirectionMask = 0x80;
    private const int DirectionShift = 7;
    private const byte TypeMask = 0x60;
    private const int TypeShift = 5;
    private const byte RecipientMask = 0x1F;
}
