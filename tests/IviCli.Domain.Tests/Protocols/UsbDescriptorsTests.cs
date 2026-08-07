using System.Text;
using IviCli.Domain.Protocols;

namespace IviCli.Domain.Tests.Protocols;

/// <summary>
/// The device every USB descriptor and control-pipe golden in this
/// folder is built from: a self-powered USBTMC-USB488 instrument with
/// one configuration, one interface, and the bulk-OUT / bulk-IN /
/// interrupt-IN endpoint trio ADR 0049 §2 requires. Shared so the
/// descriptor goldens and the endpoint-0 goldens cannot drift apart.
/// </summary>
internal static class UsbGoldenDevice
{
    internal const string Manufacturer = "ivi-cli";
    internal const string Product = "Virtual USBTMC Instrument";
    internal const string SerialNumber = "MOCK-0001";

    /// <summary>9 (configuration) + 9 (interface) + 7 * 3 (endpoints).</summary>
    internal const int ConfigurationBlobLength = 39;

    internal static UsbDeviceDefinition Definition =>
        new(
            IdVendor: 0x0B3E,
            IdProduct: 0x1042,
            BcdDevice: 0x0100,
            DeviceClass: 0x00,
            DeviceSubClass: 0x00,
            DeviceProtocol: 0x00,
            Manufacturer: Manufacturer,
            Product: Product,
            SerialNumber: SerialNumber,
            SelfPowered: true,
            Configuration: new UsbConfigurationDefinition(
                ConfigurationValue: 1,
                MaxPowerMilliamps: 100,
                Interfaces:
                [
                    new UsbInterfaceDefinition(
                        InterfaceNumber: 0,
                        InterfaceClass: 0xFE,
                        InterfaceSubClass: 0x03,
                        InterfaceProtocol: 0x01,
                        Endpoints:
                        [
                            new UsbEndpointDefinition(
                                Address: 0x01,
                                TransferType: UsbEndpointTransferType.Bulk,
                                MaxPacketSize: 512,
                                Interval: 0
                            ),
                            new UsbEndpointDefinition(
                                Address: 0x81,
                                TransferType: UsbEndpointTransferType.Bulk,
                                MaxPacketSize: 512,
                                Interval: 0
                            ),
                            new UsbEndpointDefinition(
                                Address: 0x82,
                                TransferType: UsbEndpointTransferType.Interrupt,
                                MaxPacketSize: 8,
                                Interval: 16
                            ),
                        ]
                    ),
                ]
            )
        );

    /// <summary>
    /// The 18-byte device descriptor of USB 2.0 §9.6.1. Every 2-byte
    /// field is little endian, which is what makes idVendor 0x0B3E read
    /// `3E 0B` on the wire.
    /// </summary>
    internal static byte[] DeviceDescriptor =>
        [
            0x12, // bLength = 18
            0x01, // bDescriptorType = DEVICE
            0x00, // bcdUSB lo   \_ 0x0200, little endian
            0x02, // bcdUSB hi   /
            0x00, // bDeviceClass: declared per interface
            0x00, // bDeviceSubClass
            0x00, // bDeviceProtocol
            0x40, // bMaxPacketSize0 = 64
            0x3E, // idVendor lo  \_ 0x0B3E, little endian
            0x0B, // idVendor hi  /
            0x42, // idProduct lo \_ 0x1042, little endian
            0x10, // idProduct hi /
            0x00, // bcdDevice lo \_ 0x0100, little endian
            0x01, // bcdDevice hi /
            0x01, // iManufacturer
            0x02, // iProduct
            0x03, // iSerialNumber
            0x01, // bNumConfigurations
        ];

    /// <summary>
    /// The whole configuration hierarchy of USB 2.0 §9.6.3-§9.6.6, in
    /// the order a GET_DESCRIPTOR(CONFIGURATION) returns it.
    /// </summary>
    internal static byte[] ConfigurationBlob =>
        [
            // Configuration descriptor, 9 bytes
            0x09, // bLength
            0x02, // bDescriptorType = CONFIGURATION
            0x27, // wTotalLength lo \_ 39, little endian
            0x00, // wTotalLength hi /
            0x01, // bNumInterfaces
            0x01, // bConfigurationValue
            0x00, // iConfiguration: unnamed
            0xC0, // bmAttributes: D7 reserved-one | D6 self-powered
            0x32, // bMaxPower = 50 * 2 mA = 100 mA
            // Interface descriptor, 9 bytes
            0x09, // bLength
            0x04, // bDescriptorType = INTERFACE
            0x00, // bInterfaceNumber
            0x00, // bAlternateSetting
            0x03, // bNumEndpoints
            0xFE, // bInterfaceClass: application specific
            0x03, // bInterfaceSubClass: USBTMC
            0x01, // bInterfaceProtocol: USB488
            0x00, // iInterface: unnamed
            // Endpoint descriptor: bulk OUT, 7 bytes
            0x07, // bLength
            0x05, // bDescriptorType = ENDPOINT
            0x01, // bEndpointAddress: ep 1, direction OUT
            0x02, // bmAttributes: bulk
            0x00, // wMaxPacketSize lo \_ 512, little endian
            0x02, // wMaxPacketSize hi /
            0x00, // bInterval
            // Endpoint descriptor: bulk IN, 7 bytes
            0x07,
            0x05,
            0x81, // bEndpointAddress: ep 1, direction IN (bit 7 set)
            0x02,
            0x00,
            0x02,
            0x00,
            // Endpoint descriptor: interrupt IN, 7 bytes
            0x07,
            0x05,
            0x82, // bEndpointAddress: ep 2, direction IN
            0x03, // bmAttributes: interrupt
            0x08, // wMaxPacketSize lo \_ 8, little endian
            0x00, // wMaxPacketSize hi /
            0x10, // bInterval = 16
        ];
}

/// <summary>
/// Golden-vector tests for the standard descriptor builders. Offsets and
/// field widths come from USB 2.0 chapter 9 (§9.6.1 device, §9.6.3
/// configuration, §9.6.5 interface, §9.6.6 endpoint, §9.6.7 string), so
/// a one-byte drift fails here rather than as a host that refuses to
/// enumerate the mock.
/// </summary>
public sealed class UsbDescriptorsTests
{
    [Fact]
    public void BuildDeviceDescriptor_emits_the_eighteen_byte_golden()
    {
        var descriptor = UsbDescriptors.BuildDeviceDescriptor(UsbGoldenDevice.Definition);

        descriptor.Length.ShouldBe(18);
        descriptor.ShouldBe(UsbGoldenDevice.DeviceDescriptor);
    }

    [Fact]
    public void BuildDeviceDescriptor_assigns_string_indices_deterministically()
    {
        var descriptor = UsbDescriptors.BuildDeviceDescriptor(UsbGoldenDevice.Definition);

        descriptor[14].ShouldBe(UsbDescriptors.ManufacturerStringIndex);
        descriptor[15].ShouldBe(UsbDescriptors.ProductStringIndex);
        descriptor[16].ShouldBe(UsbDescriptors.SerialNumberStringIndex);
    }

    [Fact]
    public void BuildConfigurationBlob_emits_the_whole_golden_hierarchy()
    {
        var blob = UsbDescriptors.BuildConfigurationBlob(UsbGoldenDevice.Definition);

        blob.ShouldBe(UsbGoldenDevice.ConfigurationBlob);
    }

    [Fact]
    public void BuildConfigurationBlob_totals_nine_plus_nine_plus_seven_per_endpoint()
    {
        var blob = UsbDescriptors.BuildConfigurationBlob(UsbGoldenDevice.Definition);

        // One interface carrying two bulk endpoints and one interrupt
        // endpoint: 9 + (9 + 7 * 3) = 39 bytes.
        blob.Length.ShouldBe(UsbGoldenDevice.ConfigurationBlobLength);

        // wTotalLength at offset 2 covers the whole hierarchy, not the
        // 9-byte header, and is little endian.
        blob[2].ShouldBe((byte)0x27);
        blob[3].ShouldBe((byte)0x00);
    }

    [Fact]
    public void BuildConfigurationBlob_places_class_specific_descriptors_after_their_interface()
    {
        // USB 2.0 §9.6.3: descriptors a class defines follow the
        // interface descriptor they belong to and precede that
        // interface's endpoint descriptors.
        var definition = WithClassSpecificDescriptors([
            [0x04, 0x24, 0x02, 0x02],
            [0x03, 0x24, 0x06],
        ]);

        var blob = UsbDescriptors.BuildConfigurationBlob(definition);

        blob[9..18].ShouldBe(UsbGoldenDevice.ConfigurationBlob[9..18]);
        blob[18..22].ShouldBe([0x04, 0x24, 0x02, 0x02]);
        blob[22..25].ShouldBe([0x03, 0x24, 0x06]);
        blob[25..32].ShouldBe(UsbGoldenDevice.ConfigurationBlob[18..25]);
    }

    [Fact]
    public void BuildConfigurationBlob_counts_class_specific_descriptors_in_wTotalLength()
    {
        var definition = WithClassSpecificDescriptors([
            [0x04, 0x24, 0x02, 0x02],
            [0x03, 0x24, 0x06],
        ]);

        var blob = UsbDescriptors.BuildConfigurationBlob(definition);

        var expected = UsbGoldenDevice.ConfigurationBlobLength + 7;
        UsbDescriptors.ConfigurationTotalLength(definition.Configuration).ShouldBe(expected);
        blob.Length.ShouldBe(expected);
        blob[2].ShouldBe((byte)expected);
        blob[3].ShouldBe((byte)0x00);
    }

    [Fact]
    public void BuildConfigurationBlob_emits_no_class_specific_descriptors_by_default()
    {
        // The whole addition is opt-in: an interface that declares none
        // produces the byte-for-byte hierarchy it produced before.
        UsbGoldenDevice
            .Definition.Configuration.Interfaces[0]
            .ClassSpecificDescriptors.ShouldBeEmpty();
    }

    private static UsbDeviceDefinition WithClassSpecificDescriptors(byte[][] descriptors)
    {
        var definition = UsbGoldenDevice.Definition;
        var descriptor = definition.Configuration.Interfaces[0] with
        {
            ClassSpecificDescriptors = descriptors,
        };
        return definition with
        {
            Configuration = definition.Configuration with { Interfaces = [descriptor] },
        };
    }

    [Fact]
    public void BuildConfigurationBlob_clears_the_self_powered_bit_for_a_bus_powered_device()
    {
        var busPowered = UsbGoldenDevice.Definition with { SelfPowered = false };

        var blob = UsbDescriptors.BuildConfigurationBlob(busPowered);

        // D7 is reserved and always one; D6 carries self-powered.
        blob[7].ShouldBe((byte)0x80);
    }

    [Fact]
    public void BuildStringDescriptor_index_zero_is_the_langid_table()
    {
        UsbDescriptors
            .TryBuildStringDescriptor(UsbGoldenDevice.Definition, 0, out var descriptor)
            .ShouldBeTrue();

        // bLength 4, type STRING, then wLANGID 0x0409 little endian.
        descriptor.ShouldBe([0x04, 0x03, 0x09, 0x04]);
    }

    [Fact]
    public void BuildStringDescriptor_encodes_the_product_string_as_utf16_little_endian()
    {
        UsbDescriptors
            .TryBuildStringDescriptor(
                UsbGoldenDevice.Definition,
                UsbDescriptors.ProductStringIndex,
                out var descriptor
            )
            .ShouldBeTrue();

        var expected = new byte[2 + (UsbGoldenDevice.Product.Length * 2)];
        expected[0] = (byte)expected.Length; // bLength counts the 2-byte header
        expected[1] = UsbDescriptorType.String;
        Encoding.Unicode.GetBytes(UsbGoldenDevice.Product).CopyTo(expected.AsSpan(2));

        descriptor.ShouldBe(expected);
        descriptor[0].ShouldBe((byte)52);
        descriptor[2].ShouldBe((byte)'V');
        descriptor[3].ShouldBe((byte)0x00); // the high half of the code unit
    }

    [Fact]
    public void BuildStringDescriptor_serves_manufacturer_and_serial_from_their_own_indices()
    {
        var definition = UsbGoldenDevice.Definition;

        UsbDescriptors
            .TryBuildStringDescriptor(
                definition,
                UsbDescriptors.ManufacturerStringIndex,
                out var manufacturer
            )
            .ShouldBeTrue();
        UsbDescriptors
            .TryBuildStringDescriptor(
                definition,
                UsbDescriptors.SerialNumberStringIndex,
                out var serial
            )
            .ShouldBeTrue();

        Encoding.Unicode.GetString(manufacturer.AsSpan(2)).ShouldBe(UsbGoldenDevice.Manufacturer);
        Encoding.Unicode.GetString(serial.AsSpan(2)).ShouldBe(UsbGoldenDevice.SerialNumber);
    }

    [Fact]
    public void BuildStringDescriptor_rejects_a_value_too_long_for_the_single_byte_length()
    {
        // bLength is one byte, so 126 characters plus the 2-byte header
        // is the whole budget.
        Should.NotThrow(() => UsbDescriptors.BuildStringDescriptor(new string('a', 126)));
        Should.Throw<ArgumentException>(() =>
            UsbDescriptors.BuildStringDescriptor(new string('a', 127))
        );
    }

    [Fact]
    public void TryBuildStringDescriptor_refuses_an_index_the_definition_never_assigned()
    {
        UsbDescriptors
            .TryBuildStringDescriptor(UsbGoldenDevice.Definition, 4, out var descriptor)
            .ShouldBeFalse();
        descriptor.ShouldBeEmpty();
    }
}
