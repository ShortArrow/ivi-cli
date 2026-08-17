using IviCli.Domain.Protocols;

namespace IviCli.Domain.Tests.Protocols;

/// <summary>
/// Behaviour tests for the endpoint-0 state machine: the standard device
/// requests of USB 2.0 §9.4 over the descriptor set of §9.6, and the
/// seam where a USB/IP URB (ADR 0049 §1) becomes one of them.
/// </summary>
public sealed class UsbControlPipeTests
{
    private const byte DeviceToHostStandardDevice = 0x80;
    private const byte HostToDeviceStandardDevice = 0x00;
    private const byte HostToDeviceStandardEndpoint = 0x02;
    private const byte DeviceToHostClassInterface = 0xA1;
    private const byte HostToDeviceClassInterface = 0x21;

    private static UsbControlPipe Pipe() => new(UsbGoldenDevice.Definition);

    [Fact]
    public void GetDescriptor_device_returns_the_descriptor_truncated_to_wLength()
    {
        // The host's very first probe asks for 64 bytes and gets the 18
        // the device actually has.
        var result = Pipe().Handle(GetDescriptor(UsbDescriptorType.Device, index: 0, wLength: 64));

        result.Outcome.ShouldBe(UsbControlOutcome.Handled);
        result.Data.ShouldBe(UsbGoldenDevice.DeviceDescriptor);
    }

    [Fact]
    public void GetDescriptor_device_truncates_a_short_first_probe_without_stalling()
    {
        // Some hosts read only bMaxPacketSize0 before addressing.
        var result = Pipe().Handle(GetDescriptor(UsbDescriptorType.Device, index: 0, wLength: 8));

        result.Outcome.ShouldBe(UsbControlOutcome.Handled);
        result.Data.ShouldBe(UsbGoldenDevice.DeviceDescriptor[..8]);
    }

    [Fact]
    public void GetDescriptor_configuration_answers_both_stages_of_the_host_read()
    {
        var pipe = Pipe();

        // Stage 1: nine bytes, just to learn wTotalLength.
        var header = pipe.Handle(GetDescriptor(UsbDescriptorType.Configuration, 0, wLength: 9));
        header.Outcome.ShouldBe(UsbControlOutcome.Handled);
        header.Data.Length.ShouldBe(9);
        header.Data.ShouldBe(UsbGoldenDevice.ConfigurationBlob[..9]);

        // Stage 2: the wTotalLength the first stage reported.
        var total = (ushort)(header.Data[2] | (header.Data[3] << 8));
        total.ShouldBe((ushort)UsbGoldenDevice.ConfigurationBlobLength);

        var whole = pipe.Handle(GetDescriptor(UsbDescriptorType.Configuration, 0, wLength: total));
        whole.Outcome.ShouldBe(UsbControlOutcome.Handled);
        whole.Data.ShouldBe(UsbGoldenDevice.ConfigurationBlob);
    }

    [Fact]
    public void GetDescriptor_string_returns_the_langid_table_at_index_zero()
    {
        var result = Pipe().Handle(GetDescriptor(UsbDescriptorType.String, index: 0, wLength: 255));

        result.Outcome.ShouldBe(UsbControlOutcome.Handled);
        result.Data.ShouldBe([0x04, 0x03, 0x09, 0x04]);
    }

    [Fact]
    public void GetDescriptor_stalls_on_a_string_index_the_device_does_not_have()
    {
        var result = Pipe().Handle(GetDescriptor(UsbDescriptorType.String, index: 9, wLength: 255));

        result.Outcome.ShouldBe(UsbControlOutcome.Stall);
        result.Data.ShouldBeEmpty();
    }

    [Fact]
    public void GetDescriptor_stalls_on_a_descriptor_type_endpoint_zero_never_serves()
    {
        // Interface and endpoint descriptors are only reachable inside
        // the configuration blob, never as a standalone GET_DESCRIPTOR.
        var result = Pipe()
            .Handle(GetDescriptor(UsbDescriptorType.Interface, index: 0, wLength: 9));

        result.Outcome.ShouldBe(UsbControlOutcome.Stall);
    }

    [Fact]
    public void GetDescriptor_stalls_on_a_configuration_index_beyond_the_only_configuration()
    {
        var result = Pipe()
            .Handle(GetDescriptor(UsbDescriptorType.Configuration, index: 1, wLength: 9));

        result.Outcome.ShouldBe(UsbControlOutcome.Stall);
    }

    [Fact]
    public void GetConfiguration_is_zero_until_the_host_configures_the_device()
    {
        var pipe = Pipe();

        pipe.ConfigurationValue.ShouldBe((byte)0);
        var result = pipe.Handle(
            new UsbSetupPacket(
                DeviceToHostStandardDevice,
                UsbStandardRequest.GetConfiguration,
                0,
                0,
                1
            )
        );

        result.Outcome.ShouldBe(UsbControlOutcome.Handled);
        result.Data.ShouldBe([0x00]);
    }

    [Fact]
    public void SetConfiguration_then_GetConfiguration_echoes_the_configured_value()
    {
        var pipe = Pipe();

        var set = pipe.Handle(
            new UsbSetupPacket(
                HostToDeviceStandardDevice,
                UsbStandardRequest.SetConfiguration,
                1,
                0,
                0
            )
        );
        set.Outcome.ShouldBe(UsbControlOutcome.Handled);
        set.Data.ShouldBeEmpty();
        pipe.ConfigurationValue.ShouldBe((byte)1);

        var get = pipe.Handle(
            new UsbSetupPacket(
                DeviceToHostStandardDevice,
                UsbStandardRequest.GetConfiguration,
                0,
                0,
                1
            )
        );
        get.Data.ShouldBe([0x01]);
    }

    [Fact]
    public void SetConfiguration_stalls_on_a_value_the_device_does_not_offer()
    {
        var pipe = Pipe();

        var result = pipe.Handle(
            new UsbSetupPacket(
                HostToDeviceStandardDevice,
                UsbStandardRequest.SetConfiguration,
                7,
                0,
                0
            )
        );

        result.Outcome.ShouldBe(UsbControlOutcome.Stall);
        pipe.ConfigurationValue.ShouldBe((byte)0);
    }

    [Fact]
    public void GetStatus_on_the_device_reports_the_self_powered_bit_little_endian()
    {
        var result = Pipe()
            .Handle(
                new UsbSetupPacket(
                    DeviceToHostStandardDevice,
                    UsbStandardRequest.GetStatus,
                    0,
                    0,
                    2
                )
            );

        result.Outcome.ShouldBe(UsbControlOutcome.Handled);
        result.Data.ShouldBe([0x01, 0x00]);
    }

    [Fact]
    public void GetStatus_on_a_bus_powered_device_clears_the_self_powered_bit()
    {
        var pipe = new UsbControlPipe(UsbGoldenDevice.Definition with { SelfPowered = false });

        var result = pipe.Handle(
            new UsbSetupPacket(DeviceToHostStandardDevice, UsbStandardRequest.GetStatus, 0, 0, 2)
        );

        result.Data.ShouldBe([0x00, 0x00]);
    }

    [Theory]
    [InlineData(0x81)]
    [InlineData(0x01)]
    [InlineData(0x82)]
    [InlineData(0x00)]
    public void ClearFeature_endpoint_halt_on_an_endpoint_the_device_has_is_accepted(int endpoint)
    {
        var result = Pipe()
            .Handle(
                new UsbSetupPacket(
                    HostToDeviceStandardEndpoint,
                    UsbStandardRequest.ClearFeature,
                    UsbControlPipe.FeatureEndpointHalt,
                    (ushort)endpoint,
                    0
                )
            );

        result.Outcome.ShouldBe(UsbControlOutcome.Handled);
        result.Data.ShouldBeEmpty();
    }

    [Fact]
    public void ClearFeature_endpoint_halt_on_an_endpoint_the_device_lacks_stalls()
    {
        var result = Pipe()
            .Handle(
                new UsbSetupPacket(
                    HostToDeviceStandardEndpoint,
                    UsbStandardRequest.ClearFeature,
                    UsbControlPipe.FeatureEndpointHalt,
                    0x85,
                    0
                )
            );

        result.Outcome.ShouldBe(UsbControlOutcome.Stall);
    }

    [Fact]
    public void ClearFeature_of_any_other_feature_stalls()
    {
        var result = Pipe()
            .Handle(
                new UsbSetupPacket(
                    HostToDeviceStandardDevice,
                    UsbStandardRequest.ClearFeature,
                    1,
                    0,
                    0
                )
            );

        result.Outcome.ShouldBe(UsbControlOutcome.Stall);
    }

    [Fact]
    public void GetStatus_stalls_for_recipients_endpoint_zero_does_not_model()
    {
        // 0x81 is device-to-host, standard, recipient interface.
        var result = Pipe().Handle(new UsbSetupPacket(0x81, UsbStandardRequest.GetStatus, 0, 0, 2));

        result.Outcome.ShouldBe(UsbControlOutcome.Stall);
    }

    [Fact]
    public void SetAddress_is_acknowledged_with_an_empty_data_stage()
    {
        var result = Pipe()
            .Handle(
                new UsbSetupPacket(
                    HostToDeviceStandardDevice,
                    UsbStandardRequest.SetAddress,
                    3,
                    0,
                    0
                )
            );

        result.Outcome.ShouldBe(UsbControlOutcome.Handled);
        result.Data.ShouldBeEmpty();
    }

    [Fact]
    public void An_unknown_standard_request_stalls()
    {
        // 0x0C is reserved in USB 2.0 table 9-4.
        var result = Pipe().Handle(new UsbSetupPacket(DeviceToHostStandardDevice, 0x0C, 0, 0, 0));

        result.Outcome.ShouldBe(UsbControlOutcome.Stall);
    }

    [Fact]
    public void A_class_request_is_left_for_the_class_layer_rather_than_stalled()
    {
        // USBTMC's READ_STATUS_BYTE, which Phase 3 answers.
        var result = Pipe().Handle(new UsbSetupPacket(DeviceToHostClassInterface, 128, 0, 0, 3));

        result.Outcome.ShouldBe(UsbControlOutcome.NotHandled);
        result.Data.ShouldBeEmpty();
    }

    [Fact]
    public void A_vendor_request_is_left_for_the_class_layer_too()
    {
        var result = Pipe().Handle(new UsbSetupPacket(0x40, 1, 0, 0, 0));

        result.Outcome.ShouldBe(UsbControlOutcome.NotHandled);
    }

    [Fact]
    public void HandleEp0_answers_a_get_descriptor_submit_with_the_descriptor_as_payload()
    {
        var submit = Submit(
            seqNum: 42,
            direction: UsbIpConstants.DirIn,
            setup: GetDescriptor(UsbDescriptorType.Device, 0, 64).ToArray(),
            transferBufferLength: 64
        );

        var (reply, payload) = Pipe().HandleEp0(submit, ReadOnlyMemory<byte>.Empty);

        reply.Status.ShouldBe(0);
        reply.ActualLength.ShouldBe(18);
        payload.ShouldBe(UsbGoldenDevice.DeviceDescriptor);
        UsbIpCodec.RetSubmitPayloadLength(UsbIpConstants.DirIn, reply).ShouldBe(payload.Length);
    }

    [Fact]
    public void HandleEp0_echoes_the_seqnum_and_zeroes_the_server_side_header_fields()
    {
        var submit = Submit(
            seqNum: 42,
            direction: UsbIpConstants.DirIn,
            setup: GetDescriptor(UsbDescriptorType.Device, 0, 64).ToArray(),
            transferBufferLength: 64
        );

        var (reply, _) = Pipe().HandleEp0(submit, ReadOnlyMemory<byte>.Empty);

        reply.Header.Command.ShouldBe(UsbIpConstants.RetSubmit);
        reply.Header.SeqNum.ShouldBe(42u);
        reply.Header.DevId.ShouldBe(0u);
        reply.Header.Direction.ShouldBe(0u);
        reply.Header.Ep.ShouldBe(0u);
        reply.NumberOfPackets.ShouldBe(UsbIpConstants.NumberOfPacketsNonIso);
        reply.ErrorCount.ShouldBe(0);
    }

    [Fact]
    public void HandleEp0_maps_a_stall_to_a_negative_EPIPE_completion_with_no_payload()
    {
        var submit = Submit(
            seqNum: 7,
            direction: UsbIpConstants.DirIn,
            setup: GetDescriptor(UsbDescriptorType.String, 9, 255).ToArray(),
            transferBufferLength: 255
        );

        var (reply, payload) = Pipe().HandleEp0(submit, ReadOnlyMemory<byte>.Empty);

        reply.Status.ShouldBe(UsbControlPipe.EndpointStalledStatus);
        reply.Status.ShouldBe(-32);
        reply.ActualLength.ShouldBe(0);
        payload.ShouldBeEmpty();
        reply.Header.SeqNum.ShouldBe(7u);
    }

    [Fact]
    public void HandleEp0_maps_an_unhandled_class_request_to_the_same_stall_completion()
    {
        var submit = Submit(
            seqNum: 8,
            direction: UsbIpConstants.DirIn,
            setup: new UsbSetupPacket(DeviceToHostClassInterface, 128, 0, 0, 3).ToArray(),
            transferBufferLength: 3
        );

        var (reply, payload) = Pipe().HandleEp0(submit, ReadOnlyMemory<byte>.Empty);

        reply.Status.ShouldBe(UsbControlPipe.EndpointStalledStatus);
        reply.ActualLength.ShouldBe(0);
        payload.ShouldBeEmpty();
    }

    [Fact]
    public void HandleEp0_configures_the_device_from_a_host_to_device_submit()
    {
        var pipe = Pipe();
        var submit = Submit(
            seqNum: 9,
            direction: UsbIpConstants.DirOut,
            setup: new UsbSetupPacket(
                HostToDeviceStandardDevice,
                UsbStandardRequest.SetConfiguration,
                1,
                0,
                0
            ).ToArray(),
            transferBufferLength: 0
        );

        var (reply, payload) = pipe.HandleEp0(submit, ReadOnlyMemory<byte>.Empty);

        reply.Status.ShouldBe(0);
        reply.ActualLength.ShouldBe(0);
        payload.ShouldBeEmpty();
        pipe.ConfigurationValue.ShouldBe((byte)1);
    }

    [Fact]
    public void HandleEp0_hands_a_class_request_to_the_fallback_and_answers_with_its_data()
    {
        // The composition Phase 3b needs: standard requests first, the
        // USBTMC class handler behind them, in one place rather than at
        // every call site of the URB loop.
        var submit = Submit(
            seqNum: 11,
            direction: UsbIpConstants.DirIn,
            setup: new UsbSetupPacket(
                DeviceToHostClassInterface,
                UsbTmcConstants.RequestGetCapabilities,
                0,
                0,
                UsbTmcConstants.CapabilitiesResponseSize
            ).ToArray(),
            transferBufferLength: UsbTmcConstants.CapabilitiesResponseSize
        );
        var classHandler = new UsbTmcControlHandler(new UsbTmcMessagePump(), new Usb488Notifier());

        var (reply, payload) = Pipe()
            .HandleEp0(submit, ReadOnlyMemory<byte>.Empty, classHandler.Handle);

        reply.Status.ShouldBe(0);
        reply.ActualLength.ShouldBe(UsbTmcConstants.CapabilitiesResponseSize);
        payload.Length.ShouldBe(UsbTmcConstants.CapabilitiesResponseSize);
        payload[0].ShouldBe(UsbTmcConstants.StatusSuccess);
    }

    [Fact]
    public void HandleEp0_answers_a_standard_request_without_consulting_the_fallback()
    {
        var consulted = false;
        var submit = Submit(
            seqNum: 12,
            direction: UsbIpConstants.DirIn,
            setup: GetDescriptor(UsbDescriptorType.Device, 0, 64).ToArray(),
            transferBufferLength: 64
        );

        var (reply, payload) = Pipe()
            .HandleEp0(
                submit,
                ReadOnlyMemory<byte>.Empty,
                _ =>
                {
                    consulted = true;
                    return UsbControlResult.Stall();
                }
            );

        consulted.ShouldBeFalse();
        reply.Status.ShouldBe(0);
        payload.ShouldBe(UsbGoldenDevice.DeviceDescriptor);
    }

    [Fact]
    public void HandleEp0_stalls_when_the_fallback_declines_the_class_request_too()
    {
        var submit = Submit(
            seqNum: 13,
            direction: UsbIpConstants.DirIn,
            setup: new UsbSetupPacket(DeviceToHostClassInterface, 200, 0, 0, 3).ToArray(),
            transferBufferLength: 3
        );

        var (reply, payload) = Pipe()
            .HandleEp0(submit, ReadOnlyMemory<byte>.Empty, _ => UsbControlResult.NotHandled());

        reply.Status.ShouldBe(UsbControlPipe.EndpointStalledStatus);
        reply.ActualLength.ShouldBe(0);
        payload.ShouldBeEmpty();
    }

    [Fact]
    public void HandleEp0_hands_the_out_data_stage_to_the_class_fallback()
    {
        // SET_LINE_CODING is the first class request of ADR 0049 §5 whose
        // meaning is in the OUT data stage rather than in the setup
        // packet, so the fallback has to see those bytes.
        byte[] coding = [0x00, 0xC2, 0x01, 0x00, 0x00, 0x00, 0x08];
        var received = Array.Empty<byte>();
        var submit = Submit(
            seqNum: 14,
            direction: UsbIpConstants.DirOut,
            setup: new UsbSetupPacket(HostToDeviceClassInterface, 0x20, 0, 0, 7).ToArray(),
            transferBufferLength: coding.Length
        );

        var (reply, payload) = Pipe()
            .HandleEp0(
                submit,
                coding,
                (_, outPayload) =>
                {
                    received = outPayload.ToArray();
                    return UsbControlResult.HandledEmpty();
                }
            );

        received.ShouldBe(coding);
        reply.Status.ShouldBe(0);
        payload.ShouldBeEmpty();
    }

    [Fact]
    public void HandleEp0_reports_the_data_stage_a_handled_out_transfer_accepted()
    {
        // actual_length answers "how many bytes moved", and on an OUT
        // transfer those are the ones the host sent, not the empty
        // response.
        byte[] coding = [0x00, 0xC2, 0x01, 0x00, 0x00, 0x00, 0x08];
        var submit = Submit(
            seqNum: 15,
            direction: UsbIpConstants.DirOut,
            setup: new UsbSetupPacket(HostToDeviceClassInterface, 0x20, 0, 0, 7).ToArray(),
            transferBufferLength: coding.Length
        );

        var (reply, _) = Pipe()
            .HandleEp0(submit, coding, (_, _) => UsbControlResult.HandledEmpty());

        reply.ActualLength.ShouldBe(7);
    }

    [Fact]
    public void HandleEp0_reports_zero_for_a_handled_out_transfer_with_no_data_stage()
    {
        var submit = Submit(
            seqNum: 16,
            direction: UsbIpConstants.DirOut,
            setup: new UsbSetupPacket(
                HostToDeviceStandardDevice,
                UsbStandardRequest.SetConfiguration,
                1,
                0,
                0
            ).ToArray(),
            transferBufferLength: 0
        );

        var (reply, _) = Pipe()
            .HandleEp0(submit, ReadOnlyMemory<byte>.Empty, (_, _) => UsbControlResult.Stall());

        reply.Status.ShouldBe(0);
        reply.ActualLength.ShouldBe(0);
    }

    [Fact]
    public void HandleEp0_reports_the_returned_length_for_an_in_transfer_whatever_arrived_out()
    {
        var submit = Submit(
            seqNum: 17,
            direction: UsbIpConstants.DirIn,
            setup: GetDescriptor(UsbDescriptorType.Device, 0, 64).ToArray(),
            transferBufferLength: 64
        );

        var (reply, payload) = Pipe()
            .HandleEp0(submit, new byte[] { 1, 2, 3 }, (_, _) => UsbControlResult.Stall());

        reply.ActualLength.ShouldBe(18);
        payload.ShouldBe(UsbGoldenDevice.DeviceDescriptor);
    }

    [Fact]
    public void HandleEp0_reports_zero_when_an_out_transfer_with_a_data_stage_stalls()
    {
        var submit = Submit(
            seqNum: 18,
            direction: UsbIpConstants.DirOut,
            setup: new UsbSetupPacket(HostToDeviceClassInterface, 0x20, 0, 0, 7).ToArray(),
            transferBufferLength: 7
        );

        var (reply, _) = Pipe().HandleEp0(submit, new byte[7], (_, _) => UsbControlResult.Stall());

        reply.Status.ShouldBe(UsbControlPipe.EndpointStalledStatus);
        reply.ActualLength.ShouldBe(0);
    }

    [Fact]
    public void HandleEp0_still_composes_a_setup_only_class_fallback()
    {
        // The Phase 3 overload keeps working unchanged, which is what lets
        // the USBTMC handler stay a one-argument function.
        var submit = Submit(
            seqNum: 19,
            direction: UsbIpConstants.DirIn,
            setup: new UsbSetupPacket(
                DeviceToHostClassInterface,
                UsbTmcConstants.RequestGetCapabilities,
                0,
                0,
                UsbTmcConstants.CapabilitiesResponseSize
            ).ToArray(),
            transferBufferLength: UsbTmcConstants.CapabilitiesResponseSize
        );
        var classHandler = new UsbTmcControlHandler(new UsbTmcMessagePump(), new Usb488Notifier());

        var (reply, payload) = Pipe()
            .HandleEp0(submit, ReadOnlyMemory<byte>.Empty, classHandler.Handle);

        reply.Status.ShouldBe(0);
        payload.Length.ShouldBe(UsbTmcConstants.CapabilitiesResponseSize);
    }

    private static UsbSetupPacket GetDescriptor(byte descriptorType, byte index, ushort wLength) =>
        new(
            BmRequestType: DeviceToHostStandardDevice,
            BRequest: UsbStandardRequest.GetDescriptor,
            WValue: (ushort)((descriptorType << 8) | index),
            WIndex: 0,
            WLength: wLength
        );

    private static UsbIpCmdSubmit Submit(
        uint seqNum,
        uint direction,
        byte[] setup,
        int transferBufferLength
    ) =>
        new(
            Header: new UsbIpHeaderBasic(
                Command: UsbIpConstants.CmdSubmit,
                SeqNum: seqNum,
                DevId: 0x0001_0002,
                Direction: direction,
                Ep: 0
            ),
            TransferFlags: 0,
            TransferBufferLength: transferBufferLength,
            StartFrame: 0,
            NumberOfPackets: UsbIpConstants.NumberOfPacketsNonIso,
            Interval: 0,
            Setup: setup
        );
}
