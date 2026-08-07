using IviCli.Domain.Protocols;

namespace IviCli.Domain.Tests.Protocols;

/// <summary>
/// Behaviour tests for the CDC-ACM class control requests of ADR 0049 §5:
/// the three PSTN 1.1 §6.3 requests the ACM capabilities claim, the
/// stacking contract that keeps the handler composable with the standard
/// endpoint-0 state machine, and the refusals that keep a malformed
/// request from being silently corrected.
/// </summary>
public sealed class CdcAcmControlHandlerTests
{
    private const byte HostToDeviceClassInterface = 0x21;
    private const byte DeviceToHostClassInterface = 0xA1;
    private const byte CommunicationsInterface = CdcAcmDeviceProfile.CommunicationsInterfaceNumber;
    private const byte DataInterface = CdcAcmDeviceProfile.DataInterfaceNumber;

    /// <summary>115200 8-N-1, little endian: 0x0001C200 bits per second.</summary>
    private static byte[] DefaultCoding => [0x00, 0xC2, 0x01, 0x00, 0x00, 0x00, 0x08];

    /// <summary>9600 7-E-2: 0x2580 bits per second, two stop bits, even parity.</summary>
    private static byte[] AlternateCoding => [0x80, 0x25, 0x00, 0x00, 0x02, 0x02, 0x07];

    [Fact]
    public void A_standard_request_is_left_for_the_layer_that_owns_it()
    {
        var result = new CdcAcmControlHandler().Handle(
            new UsbSetupPacket(0x80, UsbStandardRequest.GetDescriptor, 0, 0, 18),
            ReadOnlyMemory<byte>.Empty
        );

        result.Outcome.ShouldBe(UsbControlOutcome.NotHandled);
    }

    [Fact]
    public void A_vendor_request_is_left_for_the_layer_that_owns_it()
    {
        var result = new CdcAcmControlHandler().Handle(
            new UsbSetupPacket(0x40, 1, 0, 0, 0),
            ReadOnlyMemory<byte>.Empty
        );

        result.Outcome.ShouldBe(UsbControlOutcome.NotHandled);
    }

    [Fact]
    public void GetLineCoding_answers_the_default_coding_before_the_host_sets_one()
    {
        var result = new CdcAcmControlHandler().Handle(GetLineCoding(), ReadOnlyMemory<byte>.Empty);

        result.Outcome.ShouldBe(UsbControlOutcome.Handled);
        result.Data.ShouldBe(DefaultCoding);
    }

    [Fact]
    public void SetLineCoding_then_GetLineCoding_reads_back_what_the_host_wrote()
    {
        var handler = new CdcAcmControlHandler();

        var set = handler.Handle(SetLineCoding(), AlternateCoding);
        set.Outcome.ShouldBe(UsbControlOutcome.Handled);
        set.Data.ShouldBeEmpty();

        handler.LineCoding.DteRate.ShouldBe(9600u);
        handler.LineCoding.CharFormat.ShouldBe((byte)2);
        handler.LineCoding.ParityType.ShouldBe((byte)2);
        handler.LineCoding.DataBits.ShouldBe((byte)7);

        var get = handler.Handle(GetLineCoding(), ReadOnlyMemory<byte>.Empty);
        get.Data.ShouldBe(AlternateCoding);
    }

    [Fact]
    public void SetLineCoding_stalls_on_a_data_stage_of_the_wrong_length()
    {
        var handler = new CdcAcmControlHandler();

        var result = handler.Handle(SetLineCoding(), new byte[6]);

        result.Outcome.ShouldBe(UsbControlOutcome.Stall);
        handler.LineCoding.ShouldBe(CdcLineCoding.Default);
    }

    [Fact]
    public void SetLineCoding_stalls_when_the_data_stage_never_arrived()
    {
        var handler = new CdcAcmControlHandler();

        var result = handler.Handle(SetLineCoding(), ReadOnlyMemory<byte>.Empty);

        result.Outcome.ShouldBe(UsbControlOutcome.Stall);
        handler.LineCoding.ShouldBe(CdcLineCoding.Default);
    }

    [Fact]
    public void GetLineCoding_truncates_to_the_bytes_the_host_asked_for()
    {
        var result = new CdcAcmControlHandler().Handle(
            new UsbSetupPacket(
                DeviceToHostClassInterface,
                CdcAcmConstants.RequestGetLineCoding,
                0,
                CommunicationsInterface,
                4
            ),
            ReadOnlyMemory<byte>.Empty
        );

        result.Data.ShouldBe(DefaultCoding[..4]);
    }

    [Fact]
    public void SetControlLineState_records_the_two_lines_the_host_raised()
    {
        var handler = new CdcAcmControlHandler();

        var result = handler.Handle(
            SetControlLineState(
                CdcAcmConstants.ControlLineStateDtr | CdcAcmConstants.ControlLineStateRts
            ),
            ReadOnlyMemory<byte>.Empty
        );

        result.Outcome.ShouldBe(UsbControlOutcome.Handled);
        result.Data.ShouldBeEmpty();
        handler.DataTerminalReady.ShouldBeTrue();
        handler.RequestToSend.ShouldBeTrue();
    }

    [Fact]
    public void SetControlLineState_records_a_terminal_that_dropped_DTR()
    {
        var handler = new CdcAcmControlHandler();
        handler.Handle(
            SetControlLineState(CdcAcmConstants.ControlLineStateDtr),
            ReadOnlyMemory<byte>.Empty
        );

        handler.Handle(SetControlLineState(0), ReadOnlyMemory<byte>.Empty);

        handler.DataTerminalReady.ShouldBeFalse();
        handler.RequestToSend.ShouldBeFalse();
    }

    [Fact]
    public void The_control_lines_are_clear_before_the_host_raises_them()
    {
        var handler = new CdcAcmControlHandler();

        handler.DataTerminalReady.ShouldBeFalse();
        handler.RequestToSend.ShouldBeFalse();
    }

    [Fact]
    public void A_class_request_naming_the_data_interface_stalls()
    {
        // The class requests belong to the communications interface; one
        // addressed elsewhere is a host error, not a coding change.
        var handler = new CdcAcmControlHandler();

        var result = handler.Handle(
            new UsbSetupPacket(
                HostToDeviceClassInterface,
                CdcAcmConstants.RequestSetLineCoding,
                0,
                DataInterface,
                CdcLineCoding.Size
            ),
            AlternateCoding
        );

        result.Outcome.ShouldBe(UsbControlOutcome.Stall);
        handler.LineCoding.ShouldBe(CdcLineCoding.Default);
    }

    [Fact]
    public void A_class_request_addressed_to_the_device_stalls()
    {
        var result = new CdcAcmControlHandler().Handle(
            new UsbSetupPacket(
                0xA0,
                CdcAcmConstants.RequestGetLineCoding,
                0,
                CommunicationsInterface,
                CdcLineCoding.Size
            ),
            ReadOnlyMemory<byte>.Empty
        );

        result.Outcome.ShouldBe(UsbControlOutcome.Stall);
    }

    [Fact]
    public void SendBreak_stalls_because_the_capabilities_never_claimed_it()
    {
        var result = new CdcAcmControlHandler().Handle(
            new UsbSetupPacket(
                HostToDeviceClassInterface,
                CdcAcmConstants.RequestSendBreak,
                0,
                CommunicationsInterface,
                0
            ),
            ReadOnlyMemory<byte>.Empty
        );

        result.Outcome.ShouldBe(UsbControlOutcome.Stall);
    }

    [Fact]
    public void A_class_request_the_profile_does_not_implement_stalls()
    {
        // SET_COMM_FEATURE, which the ACM capabilities do not claim.
        var result = new CdcAcmControlHandler().Handle(
            new UsbSetupPacket(HostToDeviceClassInterface, 0x02, 0, CommunicationsInterface, 2),
            new byte[2]
        );

        result.Outcome.ShouldBe(UsbControlOutcome.Stall);
    }

    [Fact]
    public void The_handler_composes_behind_the_standard_requests_of_endpoint_zero()
    {
        // The whole stack as Phase 5b will submit it: a URB in, the class
        // request answered, actual_length reporting the accepted coding.
        var pipe = new UsbControlPipe(CdcAcmGoldenDevice.Definition);
        var handler = new CdcAcmControlHandler();
        var submit = new UsbIpCmdSubmit(
            Header: new UsbIpHeaderBasic(
                Command: UsbIpConstants.CmdSubmit,
                SeqNum: 21,
                DevId: 0x0001_0002,
                Direction: UsbIpConstants.DirOut,
                Ep: 0
            ),
            TransferFlags: 0,
            TransferBufferLength: CdcLineCoding.Size,
            StartFrame: 0,
            NumberOfPackets: UsbIpConstants.NumberOfPacketsNonIso,
            Interval: 0,
            Setup: SetLineCoding().ToArray()
        );

        var (reply, payload) = pipe.HandleEp0(submit, AlternateCoding, handler.Handle);

        reply.Status.ShouldBe(0);
        reply.ActualLength.ShouldBe(CdcLineCoding.Size);
        payload.ShouldBeEmpty();
        handler.LineCoding.DteRate.ShouldBe(9600u);
    }

    [Fact]
    public void CdcLineCoding_round_trips_through_its_seven_byte_form()
    {
        var coding = CdcLineCoding.Read(AlternateCoding);

        coding.ToArray().ShouldBe(AlternateCoding);
        CdcLineCoding.Default.ToArray().ShouldBe(DefaultCoding);
    }

    [Fact]
    public void CdcLineCoding_refuses_a_buffer_that_is_not_seven_bytes()
    {
        Should.Throw<InvalidDataException>(() => CdcLineCoding.Read(new byte[8]));
    }

    private static UsbSetupPacket SetLineCoding() =>
        new(
            BmRequestType: HostToDeviceClassInterface,
            BRequest: CdcAcmConstants.RequestSetLineCoding,
            WValue: 0,
            WIndex: CommunicationsInterface,
            WLength: CdcLineCoding.Size
        );

    private static UsbSetupPacket GetLineCoding() =>
        new(
            BmRequestType: DeviceToHostClassInterface,
            BRequest: CdcAcmConstants.RequestGetLineCoding,
            WValue: 0,
            WIndex: CommunicationsInterface,
            WLength: CdcLineCoding.Size
        );

    private static UsbSetupPacket SetControlLineState(ushort lines) =>
        new(
            BmRequestType: HostToDeviceClassInterface,
            BRequest: CdcAcmConstants.RequestSetControlLineState,
            WValue: lines,
            WIndex: CommunicationsInterface,
            WLength: 0
        );
}
