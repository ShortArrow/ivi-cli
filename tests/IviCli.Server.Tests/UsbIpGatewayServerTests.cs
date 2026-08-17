using System.Collections.Immutable;
using System.Text;
using IviCli.Domain;
using IviCli.Domain.Mock;
using IviCli.Domain.Protocols;
using IviCli.Domain.Servers;
using IviCli.Server.UsbIp;
using IviCli.TestKit;
using Shouldly;
using Xunit;

namespace IviCli.Server.Tests;

/// <summary>
/// The USB/IP device server of ADR 0049 §1 exercised the way the kernel
/// client exercises it, minus the kernel: the test speaks the wire
/// protocol over a real TCP connection with the Phase 1 codec, so
/// everything between <c>OP_REQ_DEVLIST</c> and a SCPI answer coming back
/// as <c>DEV_DEP_MSG_IN</c> is covered without an attach — which is what
/// the ADR's Verification section asks CI to do.
/// </summary>
public sealed class UsbIpGatewayServerTests
{
    private const string BusId = UsbIpBench.BusId;
    private const string IdnResponse = UsbIpBench.IdnResponse;

    [Fact]
    public async Task Devlist_lists_the_configured_route_as_a_usbtmc_device()
    {
        await using var bench = await UsbIpBench.StartAsync();

        var reply = await bench.Connect().RequestDevlistAsync(bench.Token);

        reply.Status.ShouldBe(UsbIpConstants.StatusOk);
        var exported = reply.Devices.ShouldHaveSingleItem();
        exported.Device.BusId.ShouldBe(BusId);
        exported.Device.BusNum.ShouldBe(1u);
        exported.Device.DevNum.ShouldBe(1u);
        exported.Device.Speed.ShouldBe(UsbIpConstants.SpeedHigh);
        exported.Device.IdVendor.ShouldBe(UsbIpGatewayServer.MockVendorId);
        exported.Device.IdProduct.ShouldBe(UsbIpGatewayServer.MockProductId);
        exported.Device.NumInterfaces.ShouldBe((byte)1);

        var descriptor = exported.Interfaces.ShouldHaveSingleItem();
        descriptor.InterfaceClass.ShouldBe(UsbTmcConstants.InterfaceClass);
        descriptor.InterfaceSubClass.ShouldBe(UsbTmcConstants.InterfaceSubClass);
        descriptor.InterfaceProtocol.ShouldBe(UsbTmcConstants.InterfaceProtocolUsb488);
    }

    [Fact]
    public async Task Import_of_a_configured_busid_succeeds()
    {
        await using var bench = await UsbIpBench.StartAsync();

        var reply = await bench.Connect().RequestImportAsync(BusId, bench.Token);

        reply.Status.ShouldBe(UsbIpConstants.StatusOk);
        reply.Device.ShouldNotBeNull().BusId.ShouldBe(BusId);
    }

    [Fact]
    public async Task Import_of_an_unknown_busid_answers_an_error_status()
    {
        await using var bench = await UsbIpBench.StartAsync();

        var reply = await bench.Connect().RequestImportAsync("9-9", bench.Token);

        reply.Status.ShouldBe(UsbIpConstants.StatusError);
        reply.Device.ShouldBeNull();
    }

    [Fact]
    public async Task Import_of_a_busid_whose_device_is_attached_answers_an_error_status()
    {
        await using var bench = await UsbIpBench.StartAsync();
        var attached = await bench.ImportAsync();
        await Enumerate(attached, bench.Token);

        var reply = await bench.Connect().RequestImportAsync(BusId, bench.Token);

        // The same reply an unknown busid gets: no device block, so the
        // client commits no port and never starts enumerating one.
        reply.Status.ShouldBe(UsbIpConstants.StatusError);
        reply.Device.ShouldBeNull();
    }

    [Fact]
    public async Task A_busid_freed_by_a_detach_can_be_imported_again()
    {
        await using var bench = await UsbIpBench.StartAsync();
        var attached = await bench.ImportAsync();
        await Enumerate(attached, bench.Token);
        attached.Dispose();

        var client = await bench.ImportWhenFreeAsync(BusId);

        var descriptor = await Enumerate(client, bench.Token);
        descriptor.Payload.Length.ShouldBe(UsbDescriptors.DeviceDescriptorLength);
    }

    [Fact]
    public async Task Two_busids_on_different_devices_are_attached_at_once_and_both_serve_traffic()
    {
        await using var bench = await UsbIpBench.StartAsync(
            (BusId, UsbExportProfile.UsbTmc, FirstDeviceName),
            (SecondBusId, UsbExportProfile.UsbTmc, SecondDeviceName)
        );

        var first = await bench.ImportAsync(BusId);
        var second = await bench.ImportAsync(SecondBusId);

        await WriteAsync(first, ":VOLT 1.000", bench.Token);
        await WriteAsync(second, ":VOLT 2.000", bench.Token);

        var one = await bench.Backend.ReadAsync(bench.DeviceNamed(FirstDeviceName), bench.Token);
        one.ShouldBeOk().ShouldBe(":VOLT 1.000");
        var other = await bench.Backend.ReadAsync(bench.DeviceNamed(SecondDeviceName), bench.Token);
        other.ShouldBeOk().ShouldBe(":VOLT 2.000");
    }

    [Fact]
    public async Task An_imported_device_enumerates_over_endpoint_zero()
    {
        await using var bench = await UsbIpBench.StartAsync();
        var client = await bench.ImportAsync();

        var descriptor = await client.ControlInAsync(
            UsbIpTestClient.DeviceToHostStandardDevice,
            UsbStandardRequest.GetDescriptor,
            wValue: UsbDescriptorType.Device << 8,
            wIndex: 0,
            wLength: 64,
            bench.Token
        );
        descriptor.Reply.Status.ShouldBe(0);
        descriptor.Payload.Length.ShouldBe(UsbDescriptors.DeviceDescriptorLength);
        BitConverter.ToUInt16(descriptor.Payload, 8).ShouldBe(UsbIpGatewayServer.MockVendorId);

        var configured = await client.ControlOutAsync(
            UsbIpTestClient.HostToDeviceStandardDevice,
            UsbStandardRequest.SetConfiguration,
            wValue: UsbTmcDeviceProfile.ConfigurationValue,
            wIndex: 0,
            bench.Token
        );
        configured.Reply.Status.ShouldBe(0);
        configured.Reply.ActualLength.ShouldBe(0);

        var capabilities = await client.ControlInAsync(
            UsbIpTestClient.DeviceToHostClassInterface,
            UsbTmcConstants.RequestGetCapabilities,
            wValue: 0,
            wIndex: UsbTmcDeviceProfile.InterfaceNumber,
            wLength: UsbTmcConstants.CapabilitiesResponseSize,
            bench.Token
        );
        capabilities.Reply.Status.ShouldBe(0);
        capabilities.Payload.Length.ShouldBe(UsbTmcConstants.CapabilitiesResponseSize);
        capabilities.Payload[0].ShouldBe(UsbTmcConstants.StatusSuccess);
        // bmIntfcCapabilities488: the interface accepts TRIGGER.
        capabilities.Payload[14].ShouldBe(UsbTmcConstants.Interface488CapabilityTrigger);
        // bmDevCapabilities488: SR1 and DT1, so a host subscribes to the
        // interrupt endpoint and may trigger the device.
        capabilities
            .Payload[15]
            .ShouldBe(
                (byte)(
                    UsbTmcConstants.Device488CapabilitySr1 | UsbTmcConstants.Device488CapabilityDt1
                )
            );
    }

    [Fact]
    public async Task A_query_travels_out_as_a_usbtmc_message_and_back_as_the_scenario_answer()
    {
        await using var bench = await UsbIpBench.StartAsync();
        var client = await bench.ImportAsync();

        var written = await client.BulkOutAsync(
            UsbTmcCodec.WriteDevDepMsgOut(
                new UsbTmcDevDepMsgOut(
                    BTag: 1,
                    EndOfMessage: true,
                    Encoding.ASCII.GetBytes("*IDN?\n")
                )
            ),
            bench.Token
        );
        written.Reply.Status.ShouldBe(0);

        var requested = await client.BulkOutAsync(
            UsbTmcCodec.WriteRequestDevDepMsgIn(
                new UsbTmcRequestDevDepMsgIn(
                    BTag: 2,
                    TransferSize: 1024,
                    TermCharEnabled: false,
                    TermChar: 0
                )
            ),
            bench.Token
        );
        requested.Reply.Status.ShouldBe(0);

        var answer = await client.BulkInAsync(bufferLength: 1024, bench.Token);
        answer.Reply.Status.ShouldBe(0);

        var message = UsbTmcCodec.ReadDevDepMsgIn(answer.Payload);
        message.BTag.ShouldBe((byte)2);
        message.EndOfMessage.ShouldBeTrue();
        Encoding.ASCII.GetString(message.Payload).ShouldBe(IdnResponse + "\n");
    }

    [Fact]
    public async Task A_bulk_in_urb_submitted_before_the_answer_exists_completes_once_it_does()
    {
        await using var bench = await UsbIpBench.StartAsync();
        var client = await bench.ImportAsync();

        // The host queues the IN URB first — the ordering the Linux
        // client actually uses, and the one a device that answers only
        // what it has already been asked would deadlock on.
        var parked = client.SubmitBulkIn(bufferLength: 1024);
        var outbound = client.SubmitBulkOut(
            UsbTmcCodec.WriteDevDepMsgOut(
                new UsbTmcDevDepMsgOut(
                    BTag: 1,
                    EndOfMessage: true,
                    Encoding.ASCII.GetBytes("*IDN?\n")
                )
            )
        );
        var request = client.SubmitBulkOut(
            UsbTmcCodec.WriteRequestDevDepMsgIn(
                new UsbTmcRequestDevDepMsgIn(2, 1024, TermCharEnabled: false, TermChar: 0)
            )
        );
        await client.FlushAsync(bench.Token);

        var first = await client.ReadSubmitReplyAsync(bench.Token);
        var second = await client.ReadSubmitReplyAsync(bench.Token);
        var third = await client.ReadSubmitReplyAsync(bench.Token);

        // The two bulk-OUT transfers are acknowledged in order; the
        // parked IN URB completes only after the transfer that made an
        // answer available.
        first.Reply.Header.SeqNum.ShouldBe(outbound);
        second.Reply.Header.SeqNum.ShouldBe(request);
        third.Reply.Header.SeqNum.ShouldBe(parked);
        third.Reply.Status.ShouldBe(0);

        var message = UsbTmcCodec.ReadDevDepMsgIn(third.Payload);
        Encoding.ASCII.GetString(message.Payload).ShouldBe(IdnResponse + "\n");
    }

    [Fact]
    public async Task A_service_request_completes_an_interrupt_urb_the_host_had_parked()
    {
        await using var bench = await UsbIpBench.StartAsync();
        var client = await bench.ImportAsync();

        var interrupt = client.SubmitInterruptIn(UsbTmcConstants.NotificationSize);
        await client.FlushAsync(bench.Token);

        // A round trip on another endpoint proves the interrupt URB
        // reached the device and parked: its reply is not the one the
        // wire carries next.
        await client.ControlInAsync(
            UsbIpTestClient.DeviceToHostStandardDevice,
            UsbStandardRequest.GetDescriptor,
            wValue: UsbDescriptorType.Device << 8,
            wIndex: 0,
            wLength: 64,
            bench.Token
        );

        // Nothing arrives from the host now. The URB completes because
        // the backend raised a service request, which is the whole point
        // of the endpoint.
        bench.Backend.RaiseServiceRequest(bench.Device.Name);

        var notification = await client.ReadSubmitReplyAsync(bench.Token);
        notification.Reply.Header.SeqNum.ShouldBe(interrupt);
        notification.Reply.Status.ShouldBe(0);
        notification.Payload.ShouldBe([
            0x81, // bNotify1: the SRQ notification of USB488 1.00 §3.4.1
            0x40, // bNotify2: the status byte, RQS set
        ]);
    }

    [Fact]
    public async Task A_rule_srq_completes_the_parked_interrupt_urb_with_the_status_byte()
    {
        await using var bench = await UsbIpBench.StartAsync();
        bench.Backend.ActivateScenario(
            MockScenario.SingleScene(
                ScenarioName.From("opc").ShouldBeOk(),
                idnDefault: null,
                rules: ImmutableArray.Create(new MockRule("*OPC", new RuleAction.Ack(), Srq: 0x60))
            ),
            bench.Device.Name
        );
        var client = await bench.ImportAsync();

        var interrupt = client.SubmitInterruptIn(UsbTmcConstants.NotificationSize);
        await client.FlushAsync(bench.Token);

        // Nothing but the rule firing can complete that URB: the host
        // sends a plain write, and the scenario says what the
        // instrument reports once it has run.
        var write = client.SubmitBulkOut(
            UsbTmcCodec.WriteDevDepMsgOut(
                new UsbTmcDevDepMsgOut(
                    BTag: 1,
                    EndOfMessage: true,
                    Encoding.ASCII.GetBytes("*OPC\n")
                )
            )
        );
        await client.FlushAsync(bench.Token);

        var first = await client.ReadSubmitReplyAsync(bench.Token);
        var second = await client.ReadSubmitReplyAsync(bench.Token);
        var acknowledged = first.Reply.Header.SeqNum == write ? first : second;
        var notification = first.Reply.Header.SeqNum == interrupt ? first : second;

        acknowledged.Reply.Header.SeqNum.ShouldBe(write);
        acknowledged.Reply.Status.ShouldBe(0);
        notification.Reply.Header.SeqNum.ShouldBe(interrupt);
        notification.Reply.Status.ShouldBe(0);
        notification.Payload.ShouldBe([
            0x81, // bNotify1: the SRQ notification of USB488 1.00 §3.4.1
            0x60, // bNotify2: the status byte the rule carries, verbatim
        ]);
    }

    [Fact]
    public async Task A_service_request_raised_before_the_urb_completes_it_on_submit()
    {
        await using var bench = await UsbIpBench.StartAsync();
        var client = await bench.ImportAsync();

        bench.Backend.RaiseServiceRequest(bench.Device.Name, statusByte: 0x50);

        var notification = await client.InterruptInAsync(
            UsbTmcConstants.NotificationSize,
            bench.Token
        );

        notification.Reply.Status.ShouldBe(0);
        notification.Payload.ShouldBe([0x81, 0x50]);
    }

    [Fact]
    public async Task A_serial_poll_answers_the_control_transfer_and_the_interrupt_endpoint()
    {
        await using var bench = await UsbIpBench.StartAsync();
        var client = await bench.ImportAsync();

        bench.Backend.RaiseServiceRequest(bench.Device.Name);
        var srq = await client.InterruptInAsync(UsbTmcConstants.NotificationSize, bench.Token);
        srq.Payload.ShouldBe([0x81, 0x40]);

        // The host queues the next interrupt URB before polling, the way
        // a driver that never wants to miss a notification does.
        var interrupt = client.SubmitInterruptIn(UsbTmcConstants.NotificationSize);
        var poll = await client.ControlInAsync(
            UsbIpTestClient.DeviceToHostClassInterface,
            UsbTmcConstants.Request488ReadStatusByte,
            wValue: 2,
            wIndex: UsbTmcDeviceProfile.InterfaceNumber,
            wLength: UsbTmcConstants.ReadStatusByteResponseSize,
            bench.Token
        );

        poll.Reply.Status.ShouldBe(0);
        poll.Payload.ShouldBe([
            0x01, // USBTMC_status = SUCCESS
            0x02, // bTag, echoed from wValue
            0x40, // the status byte
        ]);

        var answer = await client.ReadSubmitReplyAsync(bench.Token);
        answer.Reply.Header.SeqNum.ShouldBe(interrupt);
        answer.Payload.ShouldBe([
            0x82, // bNotify1: 0x80 | bTag — a READ_STATUS_BYTE answer
            0x40, // bNotify2: the status byte
        ]);
    }

    [Fact]
    public async Task A_TRIGGER_message_is_acknowledged_and_reaches_the_backend()
    {
        await using var bench = await UsbIpBench.StartAsync();
        var client = await bench.ImportAsync();

        var transfer = UsbTmcCodec.WriteTrigger(new UsbTmcTrigger(BTag: 1));
        var triggered = await client.BulkOutAsync(transfer, bench.Token);

        triggered.Reply.Status.ShouldBe(0);
        triggered.Reply.ActualLength.ShouldBe(transfer.Length);

        // The URB is acknowledged only after the trigger reached the
        // backend, so no wait is needed to observe it.
        bench.Backend.TriggerCountFor(bench.Device.Name).ShouldBe(1);
    }

    [Fact]
    public async Task Unlinking_a_parked_interrupt_urb_answers_ECONNRESET_and_never_completes_it()
    {
        await using var bench = await UsbIpBench.StartAsync();
        var client = await bench.ImportAsync();

        var interrupt = client.SubmitInterruptIn(bufferLength: 2);
        var unlink = client.SubmitUnlink(interrupt);
        await client.FlushAsync(bench.Token);

        var answer = await client.ReadUnlinkReplyAsync(bench.Token);
        answer.Header.SeqNum.ShouldBe(unlink);
        answer.Status.ShouldBe(UsbIpGatewayServer.UrbUnlinkedStatus);
        answer.Status.ShouldBe(-104);

        // Nothing is owed for the unlinked URB: the next reply on the
        // wire belongs to the request submitted after it.
        var probe = await client.ControlInAsync(
            UsbIpTestClient.DeviceToHostStandardDevice,
            UsbStandardRequest.GetDescriptor,
            wValue: UsbDescriptorType.Device << 8,
            wIndex: 0,
            wLength: 64,
            bench.Token
        );
        probe.Reply.Header.SeqNum.ShouldNotBe(interrupt);
        probe.Payload.Length.ShouldBe(UsbDescriptors.DeviceDescriptorLength);
    }

    [Fact]
    public async Task A_write_is_acknowledged_and_reaches_the_backend()
    {
        await using var bench = await UsbIpBench.StartAsync();
        var client = await bench.ImportAsync();

        var transfer = UsbTmcCodec.WriteDevDepMsgOut(
            new UsbTmcDevDepMsgOut(
                BTag: 1,
                EndOfMessage: true,
                Encoding.ASCII.GetBytes(":VOLT 24.000\n")
            )
        );
        var written = await client.BulkOutAsync(transfer, bench.Token);

        written.Reply.Status.ShouldBe(0);
        written.Reply.ActualLength.ShouldBe(transfer.Length);

        var recorded = await bench.Backend.ReadAsync(bench.Device, bench.Token);
        recorded.ShouldBeOk().ShouldBe(":VOLT 24.000");
    }

    /// <summary>
    /// One round trip on endpoint 0, which is what proves an attach is
    /// live: the import reply alone says only that the server answered.
    /// </summary>
    private static async Task<SubmitReply> Enumerate(UsbIpTestClient client, CancellationToken ct)
    {
        var descriptor = await client.ControlInAsync(
            UsbIpTestClient.DeviceToHostStandardDevice,
            UsbStandardRequest.GetDescriptor,
            wValue: UsbDescriptorType.Device << 8,
            wIndex: 0,
            wLength: 64,
            ct
        );
        descriptor.Reply.Status.ShouldBe(0);
        return descriptor;
    }

    private static async Task WriteAsync(UsbIpTestClient client, string scpi, CancellationToken ct)
    {
        var written = await client.BulkOutAsync(
            UsbTmcCodec.WriteDevDepMsgOut(
                new UsbTmcDevDepMsgOut(
                    BTag: 1,
                    EndOfMessage: true,
                    Encoding.ASCII.GetBytes(scpi + "\n")
                )
            ),
            ct
        );
        written.Reply.Status.ShouldBe(0);
    }

    private const string SecondBusId = "1-3";
    private const string FirstDeviceName = "dut_a";
    private const string SecondDeviceName = "dut_b";
}
