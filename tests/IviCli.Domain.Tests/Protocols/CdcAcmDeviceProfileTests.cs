using IviCli.Domain.Protocols;

namespace IviCli.Domain.Tests.Protocols;

/// <summary>
/// The CDC-ACM device every golden in this file is built from: the
/// serial-shaped profile of ADR 0049 §5, whose two interfaces are the
/// communications interface a host binds its serial driver to and the
/// data interface carrying the byte stream.
/// </summary>
internal static class CdcAcmGoldenDevice
{
    internal const ushort IdVendor = 0x0B3E;
    internal const ushort IdProduct = 0x1043;
    internal const ushort BcdDevice = 0x0100;
    internal const string Manufacturer = "ivi-cli";
    internal const string Product = "Virtual CDC-ACM Instrument";
    internal const string SerialNumber = "MOCK-0001";

    /// <summary>
    /// 9 (configuration) + 9 (communications interface) + 5 + 5 + 4 + 5
    /// (functional descriptors) + 7 (interrupt endpoint) + 9 (data
    /// interface) + 7 + 7 (bulk endpoints).
    /// </summary>
    internal const int ConfigurationBlobLength = 67;

    internal static UsbDeviceDefinition Definition =>
        CdcAcmDeviceProfile.Create(
            idVendor: IdVendor,
            idProduct: IdProduct,
            bcdDevice: BcdDevice,
            manufacturer: Manufacturer,
            product: Product,
            serialNumber: SerialNumber
        );

    /// <summary>
    /// The whole configuration hierarchy of USB 2.0 §9.6.3-§9.6.6 with
    /// the CDC functional descriptors of CDC 1.1 §5.2.3 in place, in the
    /// order a GET_DESCRIPTOR(CONFIGURATION) returns it.
    /// </summary>
    internal static byte[] ConfigurationBlob =>
        [
            // Configuration descriptor, 9 bytes
            0x09, // bLength
            0x02, // bDescriptorType = CONFIGURATION
            0x43, // wTotalLength lo \_ 67, little endian
            0x00, // wTotalLength hi /
            0x02, // bNumInterfaces: communications and data
            0x01, // bConfigurationValue
            0x00, // iConfiguration: unnamed
            0xC0, // bmAttributes: D7 reserved-one | D6 self-powered
            0x32, // bMaxPower = 50 * 2 mA = 100 mA
            // Communications interface descriptor, 9 bytes
            0x09, // bLength
            0x04, // bDescriptorType = INTERFACE
            0x00, // bInterfaceNumber
            0x00, // bAlternateSetting
            0x01, // bNumEndpoints: the notification endpoint alone
            0x02, // bInterfaceClass: communications
            0x02, // bInterfaceSubClass: abstract control model
            0x01, // bInterfaceProtocol: AT commands, V.250
            0x00, // iInterface: unnamed
            // Header functional descriptor, 5 bytes
            0x05, // bFunctionLength
            0x24, // bDescriptorType = CS_INTERFACE
            0x00, // bDescriptorSubtype = Header
            0x10, // bcdCDC lo \_ 0x0110, little endian
            0x01, // bcdCDC hi /
            // Call management functional descriptor, 5 bytes
            0x05, // bFunctionLength
            0x24, // bDescriptorType = CS_INTERFACE
            0x01, // bDescriptorSubtype = Call Management
            0x00, // bmCapabilities: no call management
            0x01, // bDataInterface
            // Abstract control management functional descriptor, 4 bytes
            0x04, // bFunctionLength
            0x24, // bDescriptorType = CS_INTERFACE
            0x02, // bDescriptorSubtype = ACM
            0x02, // bmCapabilities: line coding, control line state, serial state
            // Union functional descriptor, 5 bytes
            0x05, // bFunctionLength
            0x24, // bDescriptorType = CS_INTERFACE
            0x06, // bDescriptorSubtype = Union
            0x00, // bControlInterface: the communications interface
            0x01, // bSubordinateInterface0: the data interface
            // Endpoint descriptor: interrupt IN, 7 bytes
            0x07, // bLength
            0x05, // bDescriptorType = ENDPOINT
            0x82, // bEndpointAddress: ep 2, direction IN
            0x03, // bmAttributes: interrupt
            0x08, // wMaxPacketSize lo \_ 8, little endian
            0x00, // wMaxPacketSize hi /
            0x10, // bInterval = 16
            // Data interface descriptor, 9 bytes
            0x09, // bLength
            0x04, // bDescriptorType = INTERFACE
            0x01, // bInterfaceNumber
            0x00, // bAlternateSetting
            0x02, // bNumEndpoints: the bulk pair
            0x0A, // bInterfaceClass: CDC data
            0x00, // bInterfaceSubClass
            0x00, // bInterfaceProtocol: no class-specific protocol
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
        ];
}

/// <summary>
/// Tests for the CDC-ACM device profile of ADR 0049 §5. The profile is
/// checked through the descriptors a host would actually read, because
/// those bytes are the whole contract between it and a serial class
/// driver.
/// </summary>
public sealed class CdcAcmDeviceProfileTests
{
    [Fact]
    public void The_profile_descriptors_read_back_as_the_configuration_golden()
    {
        var blob = UsbDescriptors.BuildConfigurationBlob(CdcAcmGoldenDevice.Definition);

        blob.ShouldBe(CdcAcmGoldenDevice.ConfigurationBlob);
        blob.Length.ShouldBe(CdcAcmGoldenDevice.ConfigurationBlobLength);
    }

    [Fact]
    public void The_profile_declares_the_communications_class_at_the_device()
    {
        var descriptor = UsbDescriptors.BuildDeviceDescriptor(CdcAcmGoldenDevice.Definition);

        descriptor[4].ShouldBe(CdcAcmConstants.CommunicationsDeviceClass);
        descriptor[4].ShouldBe((byte)0x02);
        descriptor[5].ShouldBe((byte)0x00);
        descriptor[6].ShouldBe((byte)0x00);
    }

    [Fact]
    public void The_communications_interface_carries_the_triple_the_inbox_serial_drivers_match()
    {
        var descriptor = CdcAcmGoldenDevice.Definition.Configuration.Interfaces[0];

        descriptor.InterfaceClass.ShouldBe((byte)0x02);
        descriptor.InterfaceSubClass.ShouldBe((byte)0x02);
        descriptor.InterfaceProtocol.ShouldBe((byte)0x01);
    }

    [Fact]
    public void The_data_interface_carries_the_CDC_data_class_and_no_subclass()
    {
        var descriptor = CdcAcmGoldenDevice.Definition.Configuration.Interfaces[1];

        descriptor.InterfaceClass.ShouldBe((byte)0x0A);
        descriptor.InterfaceSubClass.ShouldBe((byte)0x00);
        descriptor.InterfaceProtocol.ShouldBe((byte)0x00);
    }

    [Fact]
    public void The_functional_descriptors_are_declared_in_the_order_CDC_defines_them()
    {
        var functional = CdcAcmGoldenDevice
            .Definition
            .Configuration
            .Interfaces[0]
            .ClassSpecificDescriptors;

        functional.Count.ShouldBe(4);
        functional[0].ShouldBe([0x05, 0x24, 0x00, 0x10, 0x01]);
        functional[1].ShouldBe([0x05, 0x24, 0x01, 0x00, 0x01]);
        functional[2].ShouldBe([0x04, 0x24, 0x02, 0x02]);
        functional[3].ShouldBe([0x05, 0x24, 0x06, 0x00, 0x01]);
    }

    [Fact]
    public void The_union_descriptor_names_the_two_interfaces_the_configuration_declares()
    {
        var definition = CdcAcmGoldenDevice.Definition;
        var union = definition.Configuration.Interfaces[0].ClassSpecificDescriptors[3];

        // A union whose master or slave named an interface that does not
        // exist is what makes a host bind the control interface and then
        // find no data pipe behind it.
        union[3].ShouldBe(definition.Configuration.Interfaces[0].InterfaceNumber);
        union[4].ShouldBe(definition.Configuration.Interfaces[1].InterfaceNumber);
    }

    [Fact]
    public void The_notification_endpoint_sits_on_the_communications_interface()
    {
        var endpoints = CdcAcmGoldenDevice.Definition.Configuration.Interfaces[0].Endpoints;

        var notification = endpoints.ShouldHaveSingleItem();
        notification.Address.ShouldBe(CdcAcmDeviceProfile.InterruptInEndpointAddress);
        notification.Direction.ShouldBe(UsbTransferDirection.DeviceToHost);
        notification.TransferType.ShouldBe(UsbEndpointTransferType.Interrupt);
        notification.MaxPacketSize.ShouldBe(CdcAcmDeviceProfile.InterruptMaxPacketSize);
        notification.Interval.ShouldBe(CdcAcmDeviceProfile.InterruptInterval);
    }

    [Fact]
    public void The_bulk_pair_sits_on_the_data_interface_at_the_only_high_speed_packet_size()
    {
        var endpoints = CdcAcmGoldenDevice.Definition.Configuration.Interfaces[1].Endpoints;

        endpoints.Count.ShouldBe(2);

        endpoints[0].Address.ShouldBe(CdcAcmDeviceProfile.BulkOutEndpointAddress);
        endpoints[0].Direction.ShouldBe(UsbTransferDirection.HostToDevice);
        endpoints[0].TransferType.ShouldBe(UsbEndpointTransferType.Bulk);

        endpoints[1].Address.ShouldBe(CdcAcmDeviceProfile.BulkInEndpointAddress);
        endpoints[1].Direction.ShouldBe(UsbTransferDirection.DeviceToHost);
        endpoints[1].TransferType.ShouldBe(UsbEndpointTransferType.Bulk);

        // High-speed bulk endpoints must use 512 (USB 2.0 §5.8.3), and
        // bMaxPacketSize0 is fixed at 64 for the same reason.
        endpoints[0].MaxPacketSize.ShouldBe((ushort)512);
        endpoints[1].MaxPacketSize.ShouldBe((ushort)512);
        CdcAcmGoldenDevice.Definition.MaxPacketSize0.ShouldBe((byte)64);
    }

    [Fact]
    public void The_profile_serves_its_identity_strings_through_endpoint_zero()
    {
        var pipe = new UsbControlPipe(CdcAcmGoldenDevice.Definition);

        var result = pipe.Handle(
            new UsbSetupPacket(
                BmRequestType: 0x80,
                BRequest: UsbStandardRequest.GetDescriptor,
                WValue: (ushort)(
                    (UsbDescriptorType.String << 8) | UsbDescriptors.SerialNumberStringIndex
                ),
                WIndex: 0,
                WLength: 255
            )
        );

        result.Outcome.ShouldBe(UsbControlOutcome.Handled);
        result.Data.ShouldBe(UsbDescriptors.BuildStringDescriptor(CdcAcmGoldenDevice.SerialNumber));
    }

    [Fact]
    public void Create_takes_the_identity_from_its_parameters()
    {
        var definition = CdcAcmDeviceProfile.Create(
            idVendor: 0x1234,
            idProduct: 0x5678,
            bcdDevice: 0x0203,
            manufacturer: "Contoso",
            product: "Serial Scope",
            serialNumber: "SN-42"
        );

        definition.IdVendor.ShouldBe((ushort)0x1234);
        definition.IdProduct.ShouldBe((ushort)0x5678);
        definition.BcdDevice.ShouldBe((ushort)0x0203);
        definition.Manufacturer.ShouldBe("Contoso");
        definition.Product.ShouldBe("Serial Scope");
        definition.SerialNumber.ShouldBe("SN-42");
    }
}
