using IviCli.Domain.Protocols;

namespace IviCli.Domain.Tests.Protocols;

/// <summary>
/// Tests for the USBTMC-USB488 device profile of ADR 0049 §2: the
/// factory turns identity parameters into the
/// <see cref="UsbDeviceDefinition"/> the Phase 2 descriptor builders and
/// endpoint-0 pipe already know how to serve, so the profile is checked
/// through the descriptors a host would actually read.
/// </summary>
public sealed class UsbTmcDeviceProfileTests
{
    private static UsbDeviceDefinition Profile() =>
        UsbTmcDeviceProfile.Create(
            idVendor: 0x0B3E,
            idProduct: 0x1042,
            bcdDevice: 0x0100,
            manufacturer: UsbGoldenDevice.Manufacturer,
            product: UsbGoldenDevice.Product,
            serialNumber: UsbGoldenDevice.SerialNumber
        );

    [Fact]
    public void Create_builds_the_device_the_descriptor_goldens_describe()
    {
        // Compared as descriptors rather than as records: the definition
        // holds its interfaces in a list, which records compare by
        // reference, and the bytes a host reads are the thing that has to
        // match anyway.
        UsbDescriptors
            .BuildDeviceDescriptor(Profile())
            .ShouldBe(UsbGoldenDevice.DeviceDescriptor);
    }

    [Fact]
    public void The_profile_declares_the_USBTMC_USB488_class_triple_on_its_one_interface()
    {
        var descriptor = Profile().Configuration.Interfaces.ShouldHaveSingleItem();

        descriptor.InterfaceClass.ShouldBe(UsbTmcConstants.InterfaceClass);
        descriptor.InterfaceSubClass.ShouldBe(UsbTmcConstants.InterfaceSubClass);
        descriptor.InterfaceProtocol.ShouldBe(UsbTmcConstants.InterfaceProtocolUsb488);
        descriptor.InterfaceClass.ShouldBe((byte)0xFE);
        descriptor.InterfaceSubClass.ShouldBe((byte)0x03);
        descriptor.InterfaceProtocol.ShouldBe((byte)0x01);
    }

    [Fact]
    public void The_profile_declares_the_class_at_the_interface_not_at_the_device()
    {
        // bDeviceClass 0 is what makes the host bind a driver per
        // interface, which is how the inbox USBTMC driver finds this
        // device.
        Profile().DeviceClass.ShouldBe((byte)0x00);
    }

    [Fact]
    public void The_profile_carries_the_bulk_pair_and_the_interrupt_notification_endpoint()
    {
        var endpoints = Profile().Configuration.Interfaces[0].Endpoints;

        endpoints.Count.ShouldBe(3);

        endpoints[0].Address.ShouldBe(UsbTmcDeviceProfile.BulkOutEndpointAddress);
        endpoints[0].Direction.ShouldBe(UsbTransferDirection.HostToDevice);
        endpoints[0].TransferType.ShouldBe(UsbEndpointTransferType.Bulk);

        endpoints[1].Address.ShouldBe(UsbTmcDeviceProfile.BulkInEndpointAddress);
        endpoints[1].Direction.ShouldBe(UsbTransferDirection.DeviceToHost);
        endpoints[1].TransferType.ShouldBe(UsbEndpointTransferType.Bulk);

        endpoints[2].Address.ShouldBe(UsbTmcDeviceProfile.InterruptInEndpointAddress);
        endpoints[2].Direction.ShouldBe(UsbTransferDirection.DeviceToHost);
        endpoints[2].TransferType.ShouldBe(UsbEndpointTransferType.Interrupt);
    }

    [Fact]
    public void The_endpoint_packet_sizes_are_the_ones_a_high_speed_device_may_report()
    {
        var endpoints = Profile().Configuration.Interfaces[0].Endpoints;

        // High-speed bulk endpoints must use 512 (USB 2.0 §5.8.3), and
        // bMaxPacketSize0 is fixed at 64 for the same reason.
        endpoints[0].MaxPacketSize.ShouldBe((ushort)512);
        endpoints[1].MaxPacketSize.ShouldBe((ushort)512);
        Profile().MaxPacketSize0.ShouldBe((byte)64);

        // The interrupt endpoint only ever carries a 2-byte USB488
        // notification, so it is sized for that and polled slowly.
        endpoints[2].MaxPacketSize.ShouldBe(UsbTmcDeviceProfile.InterruptMaxPacketSize);
        endpoints[2].Interval.ShouldBe(UsbTmcDeviceProfile.InterruptInterval);
    }

    [Fact]
    public void The_profile_descriptors_read_back_as_the_configuration_golden()
    {
        var blob = UsbDescriptors.BuildConfigurationBlob(Profile());

        blob.ShouldBe(UsbGoldenDevice.ConfigurationBlob);

        // The class triple sits at offsets 14..16 of the blob: the 9-byte
        // configuration descriptor, then bLength, bDescriptorType,
        // bInterfaceNumber, bAlternateSetting, bNumEndpoints.
        blob[14].ShouldBe(UsbTmcConstants.InterfaceClass);
        blob[15].ShouldBe(UsbTmcConstants.InterfaceSubClass);
        blob[16].ShouldBe(UsbTmcConstants.InterfaceProtocolUsb488);
    }

    [Fact]
    public void The_profile_serves_its_identity_strings_through_endpoint_zero()
    {
        var pipe = new UsbControlPipe(Profile());

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
        result.Data.ShouldBe(UsbDescriptors.BuildStringDescriptor(UsbGoldenDevice.SerialNumber));
    }

    [Fact]
    public void Create_takes_the_identity_from_its_parameters()
    {
        var definition = UsbTmcDeviceProfile.Create(
            idVendor: 0x1234,
            idProduct: 0x5678,
            bcdDevice: 0x0203,
            manufacturer: "Contoso",
            product: "Scope",
            serialNumber: "SN-42"
        );

        definition.IdVendor.ShouldBe((ushort)0x1234);
        definition.IdProduct.ShouldBe((ushort)0x5678);
        definition.BcdDevice.ShouldBe((ushort)0x0203);
        definition.Manufacturer.ShouldBe("Contoso");
        definition.Product.ShouldBe("Scope");
        definition.SerialNumber.ShouldBe("SN-42");
    }
}
