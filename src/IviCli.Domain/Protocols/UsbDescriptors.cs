using System.Text;

namespace IviCli.Domain.Protocols;

/// <summary>
/// Pure builders for the standard USB descriptors of USB 2.0 chapter 9
/// (§9.6.1 device, §9.6.3 configuration, §9.6.5 interface, §9.6.6
/// endpoint, §9.6.7 string), driven by a
/// <see cref="UsbDeviceDefinition"/> and nothing else.
///
/// Every multi-byte field inside a descriptor is <strong>little
/// endian</strong> — the opposite of the big-endian USB/IP header that
/// carries it (<see cref="UsbIpCodec"/>). The two byte orders never meet
/// in one writer: <see cref="DescriptorWriter"/> below is little endian
/// by construction, <see cref="UsbIpCodec.UsbIpWriter"/> big endian by
/// construction.
/// </summary>
public static class UsbDescriptors
{
    /// <summary>Length of a device descriptor.</summary>
    public const int DeviceDescriptorLength = 18;

    /// <summary>Length of a configuration descriptor, header only.</summary>
    public const int ConfigurationDescriptorLength = 9;

    /// <summary>Length of an interface descriptor.</summary>
    public const int InterfaceDescriptorLength = 9;

    /// <summary>Length of an endpoint descriptor.</summary>
    public const int EndpointDescriptorLength = 7;

    /// <summary>USB 2.0 in BCD, the value <c>bcdUSB</c> carries.</summary>
    public const ushort BcdUsb20 = 0x0200;

    /// <summary>The one language the mock device offers: English (United States).</summary>
    public const ushort LangIdEnglishUnitedStates = 0x0409;

    /// <summary>Index 0 is the language table, never a string.</summary>
    public const byte LangIdStringIndex = 0;

    /// <summary>Index of <see cref="UsbDeviceDefinition.Manufacturer"/>.</summary>
    public const byte ManufacturerStringIndex = 1;

    /// <summary>Index of <see cref="UsbDeviceDefinition.Product"/>.</summary>
    public const byte ProductStringIndex = 2;

    /// <summary>Index of <see cref="UsbDeviceDefinition.SerialNumber"/>.</summary>
    public const byte SerialNumberStringIndex = 3;

    /// <summary>
    /// Builds the 18-byte device descriptor. String indices are fixed
    /// (<see cref="ManufacturerStringIndex"/>,
    /// <see cref="ProductStringIndex"/>,
    /// <see cref="SerialNumberStringIndex"/>) so the descriptor and
    /// <see cref="TryBuildStringDescriptor"/> agree without carrying a
    /// table between them.
    /// </summary>
    public static byte[] BuildDeviceDescriptor(UsbDeviceDefinition definition)
    {
        var writer = new DescriptorWriter();
        writer.WriteByte(DeviceDescriptorLength);
        writer.WriteByte(UsbDescriptorType.Device);
        writer.WriteUInt16(BcdUsb20);
        writer.WriteByte(definition.DeviceClass);
        writer.WriteByte(definition.DeviceSubClass);
        writer.WriteByte(definition.DeviceProtocol);
        writer.WriteByte(definition.MaxPacketSize0);
        writer.WriteUInt16(definition.IdVendor);
        writer.WriteUInt16(definition.IdProduct);
        writer.WriteUInt16(definition.BcdDevice);
        writer.WriteByte(ManufacturerStringIndex);
        writer.WriteByte(ProductStringIndex);
        writer.WriteByte(SerialNumberStringIndex);
        writer.WriteByte(ConfigurationCount);
        return writer.ToArray();
    }

    /// <summary>
    /// Builds the whole configuration hierarchy a
    /// GET_DESCRIPTOR(CONFIGURATION) returns: the configuration
    /// descriptor, then each interface descriptor followed by its own
    /// endpoint descriptors, in declaration order.
    /// </summary>
    public static byte[] BuildConfigurationBlob(UsbDeviceDefinition definition)
    {
        var configuration = definition.Configuration;
        var writer = new DescriptorWriter();

        writer.WriteByte(ConfigurationDescriptorLength);
        writer.WriteByte(UsbDescriptorType.Configuration);
        writer.WriteUInt16((ushort)ConfigurationTotalLength(configuration));
        writer.WriteByte((byte)configuration.Interfaces.Count);
        writer.WriteByte(configuration.ConfigurationValue);
        writer.WriteByte(UnnamedStringIndex);
        writer.WriteByte(ConfigurationAttributes(definition.SelfPowered));
        writer.WriteByte((byte)(configuration.MaxPowerMilliamps / MilliampsPerPowerUnit));

        foreach (var descriptor in configuration.Interfaces)
        {
            writer.WriteByte(InterfaceDescriptorLength);
            writer.WriteByte(UsbDescriptorType.Interface);
            writer.WriteByte(descriptor.InterfaceNumber);
            writer.WriteByte(DefaultAlternateSetting);
            writer.WriteByte((byte)descriptor.Endpoints.Count);
            writer.WriteByte(descriptor.InterfaceClass);
            writer.WriteByte(descriptor.InterfaceSubClass);
            writer.WriteByte(descriptor.InterfaceProtocol);
            writer.WriteByte(UnnamedStringIndex);

            foreach (var endpoint in descriptor.Endpoints)
            {
                writer.WriteByte(EndpointDescriptorLength);
                writer.WriteByte(UsbDescriptorType.Endpoint);
                writer.WriteByte(endpoint.Address);
                writer.WriteByte((byte)endpoint.TransferType);
                writer.WriteUInt16(endpoint.MaxPacketSize);
                writer.WriteByte(endpoint.Interval);
            }
        }

        return writer.ToArray();
    }

    /// <summary>
    /// <c>wTotalLength</c>: the configuration descriptor plus every
    /// interface and endpoint descriptor beneath it.
    /// </summary>
    public static int ConfigurationTotalLength(UsbConfigurationDefinition configuration)
    {
        var total = ConfigurationDescriptorLength;
        foreach (var descriptor in configuration.Interfaces)
        {
            total +=
                InterfaceDescriptorLength + (descriptor.Endpoints.Count * EndpointDescriptorLength);
        }
        return total;
    }

    /// <summary>
    /// Builds the string descriptor at <paramref name="index"/>: the
    /// language table at index 0, otherwise the UTF-16LE string the
    /// device descriptor points at. Returns false for any other index,
    /// which the caller answers with a stall.
    /// </summary>
    public static bool TryBuildStringDescriptor(
        UsbDeviceDefinition definition,
        byte index,
        out byte[] descriptor
    )
    {
        descriptor = index switch
        {
            LangIdStringIndex => BuildLangIdDescriptor(),
            ManufacturerStringIndex => BuildStringDescriptor(definition.Manufacturer),
            ProductStringIndex => BuildStringDescriptor(definition.Product),
            SerialNumberStringIndex => BuildStringDescriptor(definition.SerialNumber),
            _ => [],
        };
        return descriptor.Length > 0;
    }

    /// <summary>
    /// Builds the index-0 descriptor: the table of supported language
    /// IDs, each a little-endian 16-bit code.
    /// </summary>
    public static byte[] BuildLangIdDescriptor()
    {
        var writer = new DescriptorWriter();
        writer.WriteByte(LangIdDescriptorLength);
        writer.WriteByte(UsbDescriptorType.String);
        writer.WriteUInt16(LangIdEnglishUnitedStates);
        return writer.ToArray();
    }

    /// <summary>
    /// Builds a UTF-16LE string descriptor: a 2-byte header of total
    /// length and descriptor type, then the encoded characters.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The value does not fit the descriptor's single-byte length field.
    /// </exception>
    public static byte[] BuildStringDescriptor(string value)
    {
        var encoded = Encoding.Unicode.GetBytes(value);
        var length = StringDescriptorHeaderLength + encoded.Length;
        if (length > byte.MaxValue)
        {
            throw new ArgumentException(
                $"String descriptor of {length} bytes overflows the single-byte bLength field",
                nameof(value)
            );
        }

        var writer = new DescriptorWriter();
        writer.WriteByte((byte)length);
        writer.WriteByte(UsbDescriptorType.String);
        writer.WriteBytes(encoded);
        return writer.ToArray();
    }

    /// <summary>
    /// Little-endian byte sink for descriptor bodies. Deliberately
    /// separate from <see cref="UsbIpCodec.UsbIpWriter"/>, whose
    /// integers are big endian, so neither can be reached by accident
    /// from the other's layer.
    /// </summary>
    private sealed class DescriptorWriter
    {
        private readonly List<byte> _bytes = [];

        public void WriteByte(byte value) => _bytes.Add(value);

        public void WriteUInt16(ushort value)
        {
            _bytes.Add((byte)(value & 0xFF));
            _bytes.Add((byte)(value >> 8));
        }

        public void WriteBytes(ReadOnlySpan<byte> bytes)
        {
            for (var i = 0; i < bytes.Length; i++)
            {
                _bytes.Add(bytes[i]);
            }
        }

        public byte[] ToArray() => _bytes.ToArray();
    }

    /// <summary>
    /// <c>bmAttributes</c>: D7 is reserved and must be one, D6 reports a
    /// self-powered device, D5 remote wakeup — which the mock does not
    /// support.
    /// </summary>
    private static byte ConfigurationAttributes(bool selfPowered) =>
        (byte)(ReservedAttribute | (selfPowered ? SelfPoweredAttribute : 0));

    /// <summary>One configuration; ADR 0049 §6 keeps it that way.</summary>
    private const byte ConfigurationCount = 1;

    private const byte DefaultAlternateSetting = 0;

    private const byte UnnamedStringIndex = 0;

    private const byte ReservedAttribute = 0x80;

    private const byte SelfPoweredAttribute = 0x40;

    private const int MilliampsPerPowerUnit = 2;

    private const byte LangIdDescriptorLength = 4;

    private const int StringDescriptorHeaderLength = 2;
}
