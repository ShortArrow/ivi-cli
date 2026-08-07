using System.Buffers.Binary;
using IviCli.Domain.Protocols;

namespace IviCli.Domain.Tests.Protocols;

/// <summary>
/// Golden-vector tests for the 8-byte SETUP packet of USB 2.0 §9.3.
/// The endianness contrast with <see cref="UsbIpCodec"/> is deliberate
/// and pinned here: USB/IP headers are big endian, the SETUP fields the
/// same message carries are little endian, and a codec that confuses the
/// two enumerates as a device asking for descriptor 0x0001.
/// </summary>
public sealed class UsbSetupPacketTests
{
    /// <summary>
    /// The canonical first request of every enumeration:
    /// GET_DESCRIPTOR(DEVICE), 64 bytes requested.
    /// </summary>
    private static readonly byte[] GetDescriptorDevice =
    [
        0x80, // bmRequestType: device-to-host, standard, device
        0x06, // bRequest: GET_DESCRIPTOR
        0x00, // wValue lo: descriptor index 0
        0x01, // wValue hi: descriptor type DEVICE
        0x00, // wIndex lo
        0x00, // wIndex hi
        0x40, // wLength lo: 64
        0x00, // wLength hi
    ];

    [Fact]
    public void Read_parses_the_canonical_get_descriptor_device_setup()
    {
        var packet = UsbSetupPacket.Read(GetDescriptorDevice);

        packet.BmRequestType.ShouldBe((byte)0x80);
        packet.BRequest.ShouldBe(UsbStandardRequest.GetDescriptor);
        packet.WValue.ShouldBe((ushort)0x0100);
        packet.WIndex.ShouldBe((ushort)0x0000);
        packet.WLength.ShouldBe((ushort)64);
    }

    [Fact]
    public void Read_decodes_bmRequestType_into_direction_type_and_recipient()
    {
        var packet = UsbSetupPacket.Read(GetDescriptorDevice);

        packet.Direction.ShouldBe(UsbTransferDirection.DeviceToHost);
        packet.Type.ShouldBe(UsbRequestType.Standard);
        packet.Recipient.ShouldBe(UsbRecipient.Device);
    }

    [Fact]
    public void Read_decodes_wValue_little_endian_where_the_usbip_header_is_big_endian()
    {
        // Bytes 2..3 are `00 01`. USB 2.0 §9.1 fixes every multi-byte
        // SETUP field little endian, so wValue is 0x0100 (type DEVICE,
        // index 0). Read big endian — the order USB/IP headers use — the
        // same two bytes would say 0x0001.
        var packet = UsbSetupPacket.Read(GetDescriptorDevice);

        packet.WValue.ShouldBe((ushort)0x0100);
        BinaryPrimitives.ReadUInt16BigEndian(GetDescriptorDevice.AsSpan(2, 2)).ShouldBe((ushort)1);
    }

    [Fact]
    public void DescriptorType_and_DescriptorIndex_split_wValue_high_and_low()
    {
        var packet = UsbSetupPacket.Read([0x80, 0x06, 0x02, 0x03, 0x09, 0x04, 0xFF, 0x00]);

        packet.DescriptorType.ShouldBe(UsbDescriptorType.String);
        packet.DescriptorIndex.ShouldBe((byte)2);
        packet.WIndex.ShouldBe((ushort)0x0409); // the langid, also little endian
    }

    [Fact]
    public void Write_round_trips_the_canonical_setup()
    {
        var packet = UsbSetupPacket.Read(GetDescriptorDevice);

        packet.ToArray().ShouldBe(GetDescriptorDevice);
    }

    [Fact]
    public void Write_emits_a_host_to_device_setup_with_little_endian_fields()
    {
        // SET_CONFIGURATION(1): host-to-host direction bit clear, no
        // data stage.
        var packet = new UsbSetupPacket(
            BmRequestType: 0x00,
            BRequest: UsbStandardRequest.SetConfiguration,
            WValue: 0x0001,
            WIndex: 0x0000,
            WLength: 0x0000
        );

        packet.ToArray().ShouldBe([0x00, 0x09, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00]);
    }

    [Theory]
    [InlineData(
        0xA1,
        UsbTransferDirection.DeviceToHost,
        UsbRequestType.Class,
        UsbRecipient.Interface
    )]
    [InlineData(
        0x21,
        UsbTransferDirection.HostToDevice,
        UsbRequestType.Class,
        UsbRecipient.Interface
    )]
    [InlineData(
        0x40,
        UsbTransferDirection.HostToDevice,
        UsbRequestType.Vendor,
        UsbRecipient.Device
    )]
    [InlineData(
        0x02,
        UsbTransferDirection.HostToDevice,
        UsbRequestType.Standard,
        UsbRecipient.Endpoint
    )]
    public void BmRequestType_bitfields_follow_the_spec_encoding(
        byte bmRequestType,
        UsbTransferDirection direction,
        UsbRequestType type,
        UsbRecipient recipient
    )
    {
        var packet = new UsbSetupPacket(bmRequestType, 0, 0, 0, 0);

        packet.Direction.ShouldBe(direction);
        packet.Type.ShouldBe(type);
        packet.Recipient.ShouldBe(recipient);
    }

    [Fact]
    public void Read_rejects_a_setup_field_that_is_not_eight_bytes()
    {
        Should.Throw<InvalidDataException>(() => UsbSetupPacket.Read(new byte[7]));
    }

    [Fact]
    public void Write_rejects_a_destination_shorter_than_the_setup_field()
    {
        var packet = UsbSetupPacket.Read(GetDescriptorDevice);

        Should.Throw<ArgumentException>(() => packet.Write(new byte[7]));
    }
}
