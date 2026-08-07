using IviCli.Domain.Protocols;

namespace IviCli.Domain.Tests.Protocols;

/// <summary>
/// Behaviour tests for the USB488 service-request state of ADR 0049 §2:
/// the cached status byte, the SRQ condition, and the two-byte
/// notifications the interrupt-IN endpoint carries.
///
/// The packet layouts are USB488 1.00 §3.4.1 read the way the Linux
/// <c>usbtmc.c</c> driver reads them — <c>bNotify1</c> 0x81 is an SRQ and
/// anything above it is a READ_STATUS_BYTE answer, which is what keeps
/// <c>bTag</c> 1 out of the serial-poll range.
/// </summary>
public sealed class Usb488NotifierTests
{
    private const byte RequestService = 0x40;
    private const byte MessageAvailable = 0x10;
    private const byte SerialPollTag = 2;

    [Fact]
    public void A_service_request_queues_the_SRQ_notification_of_USB488_3_4_1()
    {
        var notifier = new Usb488Notifier();

        notifier.RaiseServiceRequest(RequestService);

        notifier.TryTakeNotification(out var packet).ShouldBeTrue();
        packet.ShouldBe([
            0x81, // bNotify1: bit 7 set, bTag 1 — the SRQ notification
            0x40, // bNotify2: the status byte, RQS set
        ]);
    }

    [Fact]
    public void A_device_that_raised_nothing_has_no_notification_to_give()
    {
        new Usb488Notifier().TryTakeNotification(out _).ShouldBeFalse();
    }

    [Fact]
    public void A_notification_is_taken_once()
    {
        var notifier = new Usb488Notifier();
        notifier.RaiseServiceRequest(RequestService);

        notifier.TryTakeNotification(out _).ShouldBeTrue();

        notifier.TryTakeNotification(out _).ShouldBeFalse();
    }

    [Fact]
    public void A_second_service_request_before_the_poll_updates_the_status_byte_only()
    {
        var notifier = new Usb488Notifier();
        notifier.RaiseServiceRequest(RequestService);

        notifier.RaiseServiceRequest((byte)(RequestService | MessageAvailable));

        // One SRQ condition, one notification: the request line was
        // already asserted and asserting it again is not an event.
        notifier.TryTakeNotification(out _).ShouldBeTrue();
        notifier.TryTakeNotification(out _).ShouldBeFalse();

        // The status the host will read is the newer one all the same.
        notifier
            .ReadStatusByte(SerialPollTag)[2]
            .ShouldBe((byte)(RequestService | MessageAvailable));
    }

    [Fact]
    public void A_service_request_after_a_poll_is_a_new_condition()
    {
        var notifier = new Usb488Notifier();
        notifier.RaiseServiceRequest(RequestService);
        notifier.TryTakeNotification(out _).ShouldBeTrue();
        notifier.ReadStatusByte(SerialPollTag);
        notifier.TryTakeNotification(out _).ShouldBeTrue();

        notifier.RaiseServiceRequest(RequestService);

        notifier.TryTakeNotification(out var packet).ShouldBeTrue();
        packet.ShouldBe([0x81, RequestService]);
    }

    [Fact]
    public void ReadStatusByte_answers_the_control_transfer_with_status_tag_and_stb()
    {
        var notifier = new Usb488Notifier();
        notifier.RaiseServiceRequest(RequestService);

        var response = notifier.ReadStatusByte(SerialPollTag);

        response.ShouldBe([
            0x01, // USBTMC_status = SUCCESS
            0x02, // bTag, echoed from wValue
            0x40, // the status byte, for a host that never claimed the
            //       interrupt endpoint
        ]);
    }

    [Fact]
    public void ReadStatusByte_queues_its_own_notification_carrying_the_tag()
    {
        var notifier = new Usb488Notifier();
        notifier.RaiseServiceRequest(RequestService);
        notifier.TryTakeNotification(out _).ShouldBeTrue();

        notifier.ReadStatusByte(SerialPollTag);

        notifier.TryTakeNotification(out var packet).ShouldBeTrue();
        packet.ShouldBe([
            0x82, // bNotify1: 0x80 | bTag 2 — above 0x81, hence a
            //       READ_STATUS_BYTE answer rather than an SRQ
            0x40, // bNotify2: the status byte
        ]);
    }

    [Fact]
    public void Reading_the_status_byte_clears_RQS_and_leaves_the_rest_standing()
    {
        var notifier = new Usb488Notifier();
        notifier.RaiseServiceRequest((byte)(RequestService | MessageAvailable));

        notifier
            .ReadStatusByte(SerialPollTag)[2]
            .ShouldBe((byte)(RequestService | MessageAvailable));

        // The serial poll consumed the request; MAV describes the output
        // queue and no poll empties that.
        notifier.ReadStatusByte(3)[2].ShouldBe(MessageAvailable);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(128)]
    [InlineData(255)]
    public void A_tag_outside_the_serial_poll_range_is_not_one_this_device_answers(ushort bTag)
    {
        Usb488Notifier.IsReadableStatusByteTag(bTag).ShouldBeFalse();
    }

    [Theory]
    [InlineData(2)]
    [InlineData(64)]
    [InlineData(127)]
    public void A_tag_inside_the_serial_poll_range_is_answerable(ushort bTag)
    {
        Usb488Notifier.IsReadableStatusByteTag(bTag).ShouldBeTrue();
    }

    [Fact]
    public void ReadStatusByte_refuses_a_tag_the_notification_format_cannot_carry()
    {
        // bTag 1 would build 0x81, which every host reads as an SRQ.
        Should.Throw<ArgumentOutOfRangeException>(() => new Usb488Notifier().ReadStatusByte(1));
    }
}
