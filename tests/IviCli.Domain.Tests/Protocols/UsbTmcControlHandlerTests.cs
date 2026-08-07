using IviCli.Domain.Protocols;

namespace IviCli.Domain.Tests.Protocols;

/// <summary>
/// Behaviour tests for the USBTMC class control requests of ADR 0049 §2,
/// the layer that takes over where <see cref="UsbControlPipe"/> answers
/// <see cref="UsbControlOutcome.NotHandled"/>.
///
/// Request codes, recipients and response layouts follow USBTMC 1.00
/// §4.2 — the same shapes the Linux <c>usbtmc.c</c> driver issues, which
/// is what the inbox class driver will send at the mock.
/// </summary>
public sealed class UsbTmcControlHandlerTests
{
    /// <summary>Device-to-host, class, recipient interface — USBTMC 1.00 §4.2.1.</summary>
    private const byte DeviceToHostClassInterface = 0xA1;

    /// <summary>Device-to-host, class, recipient endpoint — the abort requests.</summary>
    private const byte DeviceToHostClassEndpoint = 0xA2;

    private const byte HostToDeviceStandardDevice = 0x00;

    private const byte BulkOutEndpoint = 0x01;
    private const byte BulkInEndpoint = 0x81;

    private static UsbTmcControlHandler Handler() => new(new UsbTmcMessagePump());

    [Fact]
    public void GetCapabilities_answers_the_twenty_four_byte_golden()
    {
        var result = Handler()
            .Handle(
                Setup(
                    DeviceToHostClassInterface,
                    UsbTmcConstants.RequestGetCapabilities,
                    wValue: 0,
                    wIndex: 0,
                    wLength: 0x18
                )
            );

        result.Outcome.ShouldBe(UsbControlOutcome.Handled);
        result.Data.ShouldBe([
            0x01, // USBTMC_status = SUCCESS
            0x00, // reserved
            0x00, // bcdUSBTMC lo \_ 0x0100, little endian
            0x01, // bcdUSBTMC hi /
            0x00, // bmIntfcCapabilities: no listen-only (b0), no
            //       talk-only (b1), no indicator pulse (b2)
            0x00, // bmDevCapabilities: cannot end a Bulk-IN on a
            //       termination character (b0)
            0x00, // reserved \
            0x00, // reserved  |
            0x00, // reserved  |_ USBTMC reserved, offsets 6..11
            0x00, // reserved  |
            0x00, // reserved  |
            0x00, // reserved /
            0x00, // bcdUSB488 lo \_ 0x0100, little endian
            0x01, // bcdUSB488 hi /
            0x00, // bmIntfcCapabilities488: no trigger (b0), no
            //       REN/GTL/LLO (b1), not 488.2 (b2)
            0x00, // bmDevCapabilities488: DT0 (b0), RL0 (b1),
            //       **SR0** (b2), no SCPI (b3)
            0x00, // reserved \
            0x00, // reserved  |
            0x00, // reserved  |
            0x00, // reserved  |_ USB488 reserved, offsets 16..23
            0x00, // reserved  |
            0x00, // reserved  |
            0x00, // reserved  |
            0x00, // reserved /
        ]);
    }

    [Fact]
    public void GetCapabilities_declares_SR0_until_the_interrupt_in_path_exists()
    {
        var result = Handler()
            .Handle(
                Setup(
                    DeviceToHostClassInterface,
                    UsbTmcConstants.RequestGetCapabilities,
                    0,
                    0,
                    0x18
                )
            );

        // Offset 15 is bmDevCapabilities488; bit 2 is SR1. Phase 4 turns
        // it on together with the interrupt-IN SRQ endpoint, and this
        // assertion is what will fail when it does.
        (result.Data[15] & UsbTmcConstants.Device488CapabilitySr1).ShouldBe(0);
    }

    [Fact]
    public void InitiateClear_reports_success()
    {
        var result = Handler()
            .Handle(
                Setup(DeviceToHostClassInterface, UsbTmcConstants.RequestInitiateClear, 0, 0, 0x01)
            );

        result.Outcome.ShouldBe(UsbControlOutcome.Handled);
        result.Data.ShouldBe([UsbTmcConstants.StatusSuccess]);
    }

    [Fact]
    public void InitiateClear_drops_a_message_the_pump_was_still_accumulating()
    {
        var pump = new UsbTmcMessagePump();
        var handler = new UsbTmcControlHandler(pump);

        // A first transfer without EOM: the message is half-delivered.
        pump.SubmitBulkOut(
                UsbTmcCodec.WriteDevDepMsgOut(
                    new UsbTmcDevDepMsgOut(BTag: 1, EndOfMessage: false, Payload: [0x2A, 0x49])
                )
            )
            .Outcome.ShouldBe(UsbTmcBulkOutOutcome.Accumulated);

        handler.Handle(
            Setup(DeviceToHostClassInterface, UsbTmcConstants.RequestInitiateClear, 0, 0, 0x01)
        );

        // What the host sends next is a whole message, not a message with
        // the abandoned prefix glued to its front.
        var result = pump.SubmitBulkOut(
            UsbTmcCodec.WriteDevDepMsgOut(
                new UsbTmcDevDepMsgOut(
                    BTag: 4,
                    EndOfMessage: true,
                    Payload: UsbTmcGoldenTransfers.IdnQuery
                )
            )
        );

        result.Outcome.ShouldBe(UsbTmcBulkOutOutcome.MessageComplete);
        result.Message!.Value.Content.ShouldBe(UsbTmcGoldenTransfers.IdnQuery);
    }

    [Fact]
    public void CheckClearStatus_reports_success_with_nothing_left_to_clear()
    {
        var result = Handler()
            .Handle(
                Setup(
                    DeviceToHostClassInterface,
                    UsbTmcConstants.RequestCheckClearStatus,
                    0,
                    0,
                    0x02
                )
            );

        result.Outcome.ShouldBe(UsbControlOutcome.Handled);
        result.Data.ShouldBe([
            0x01, // USBTMC_status = SUCCESS
            0x00, // bmClear: bit 0 clear, no Bulk-IN data queued
        ]);
    }

    [Fact]
    public void InitiateAbortBulkOut_reports_that_no_transfer_is_in_progress()
    {
        var result = Handler()
            .Handle(
                Setup(
                    DeviceToHostClassEndpoint,
                    UsbTmcConstants.RequestInitiateAbortBulkOut,
                    wValue: 0x05, // the bTag the host wants aborted
                    wIndex: BulkOutEndpoint,
                    wLength: 0x02
                )
            );

        result.Outcome.ShouldBe(UsbControlOutcome.Handled);
        result.Data.ShouldBe([
            0x81, // USBTMC_status = TRANSFER_NOT_IN_PROGRESS
            0x05, // bTag, echoed from wValue
        ]);
    }

    [Fact]
    public void CheckAbortBulkOutStatus_reports_that_no_split_is_in_progress()
    {
        var result = Handler()
            .Handle(
                Setup(
                    DeviceToHostClassEndpoint,
                    UsbTmcConstants.RequestCheckAbortBulkOutStatus,
                    0,
                    BulkOutEndpoint,
                    0x08
                )
            );

        result.Outcome.ShouldBe(UsbControlOutcome.Handled);
        result.Data.ShouldBe([
            0x82, // USBTMC_status = SPLIT_NOT_IN_PROGRESS
            0x00, // reserved \
            0x00, // reserved  |_ offsets 1..3
            0x00, // reserved /
            0x00, // NBYTES_RXD byte 0 \
            0x00, // NBYTES_RXD byte 1  |_ 0, little endian
            0x00, // NBYTES_RXD byte 2  |
            0x00, // NBYTES_RXD byte 3 /
        ]);
    }

    [Fact]
    public void InitiateAbortBulkIn_reports_that_no_transfer_is_in_progress()
    {
        var result = Handler()
            .Handle(
                Setup(
                    DeviceToHostClassEndpoint,
                    UsbTmcConstants.RequestInitiateAbortBulkIn,
                    wValue: 0x07,
                    wIndex: BulkInEndpoint,
                    wLength: 0x02
                )
            );

        result.Data.ShouldBe([0x81, 0x07]);
    }

    [Fact]
    public void CheckAbortBulkInStatus_reports_that_no_split_is_in_progress()
    {
        var result = Handler()
            .Handle(
                Setup(
                    DeviceToHostClassEndpoint,
                    UsbTmcConstants.RequestCheckAbortBulkInStatus,
                    0,
                    BulkInEndpoint,
                    0x08
                )
            );

        result.Data.ShouldBe([
            0x82, // USBTMC_status = SPLIT_NOT_IN_PROGRESS
            0x00, // bmAbortBulkIn: bit 0 clear, nothing queued
            0x00, // reserved \_ offsets 2..3
            0x00, // reserved /
            0x00, // NBYTES_TXD byte 0 \
            0x00, // NBYTES_TXD byte 1  |_ 0, little endian
            0x00, // NBYTES_TXD byte 2  |
            0x00, // NBYTES_TXD byte 3 /
        ]);
    }

    [Fact]
    public void IndicatorPulse_stalls_because_the_capabilities_deny_it()
    {
        var result = Handler()
            .Handle(
                Setup(DeviceToHostClassInterface, UsbTmcConstants.RequestIndicatorPulse, 0, 0, 0x01)
            );

        result.Outcome.ShouldBe(UsbControlOutcome.Stall);
        result.Data.ShouldBeEmpty();
    }

    [Fact]
    public void A_class_request_the_device_does_not_implement_stalls()
    {
        // READ_STATUS_BYTE is USB488 and arrives in Phase 4.
        var result = Handler().Handle(Setup(DeviceToHostClassInterface, 128, 0, 0, 0x03));

        result.Outcome.ShouldBe(UsbControlOutcome.Stall);
    }

    [Fact]
    public void A_class_request_addressed_to_the_wrong_recipient_stalls()
    {
        // GET_CAPABILITIES is an interface request; 0xA0 addresses the
        // device.
        var result = Handler()
            .Handle(Setup(0xA0, UsbTmcConstants.RequestGetCapabilities, 0, 0, 0x18));

        result.Outcome.ShouldBe(UsbControlOutcome.Stall);
    }

    [Fact]
    public void An_abort_request_addressed_to_the_interface_stalls()
    {
        // The four abort requests are endpoint requests (USBTMC 1.00
        // §4.2.1.2); wIndex names the endpoint, not the interface.
        var result = Handler()
            .Handle(
                Setup(
                    DeviceToHostClassInterface,
                    UsbTmcConstants.RequestInitiateAbortBulkOut,
                    0x05,
                    0,
                    0x02
                )
            );

        result.Outcome.ShouldBe(UsbControlOutcome.Stall);
    }

    [Fact]
    public void A_standard_request_is_left_to_the_pipe_below_rather_than_stalled()
    {
        var result = Handler()
            .Handle(
                Setup(HostToDeviceStandardDevice, UsbStandardRequest.SetConfiguration, 1, 0, 0)
            );

        result.Outcome.ShouldBe(UsbControlOutcome.NotHandled);
    }

    [Fact]
    public void A_response_longer_than_wLength_is_truncated_rather_than_stalled()
    {
        var result = Handler()
            .Handle(
                Setup(DeviceToHostClassInterface, UsbTmcConstants.RequestGetCapabilities, 0, 0, 4)
            );

        result.Outcome.ShouldBe(UsbControlOutcome.Handled);
        result.Data.ShouldBe([0x01, 0x00, 0x00, 0x01]);
    }

    private static UsbSetupPacket Setup(
        byte bmRequestType,
        byte bRequest,
        ushort wValue,
        ushort wIndex,
        ushort wLength
    ) => new(bmRequestType, bRequest, wValue, wIndex, wLength);
}
