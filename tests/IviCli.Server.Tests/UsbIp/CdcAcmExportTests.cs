using System.Text;
using IviCli.Domain.Protocols;
using IviCli.Domain.Servers;
using IviCli.Server.UsbIp;
using IviCli.TestKit;
using Shouldly;
using Xunit;

namespace IviCli.Server.Tests;

/// <summary>
/// A route that asks for the CDC-ACM profile (ADR 0049 §5) exercised over
/// the wire the way the USBTMC export already is: the device a host
/// enumerates is a communications device, endpoint 0 answers the PSTN
/// class requests, and the bulk pair carries SCPI lines under the
/// framing rule the SOCKET gateway uses — a line ends at a newline, a
/// trailing <c>?</c> makes it a query, and a blank line is nothing.
/// </summary>
public sealed class CdcAcmExportTests
{
    private const int ConfigurationBlobLength = 67;
    private const int LineCodingLength = 7;

    [Fact]
    public void Both_profiles_put_bulk_on_endpoint_one_and_the_interrupt_on_endpoint_two()
    {
        // The server dispatches URBs by endpoint number before it knows
        // which profile the attach carries, which only works while the
        // two profiles agree. They were chosen independently, so the
        // agreement is a coincidence and is pinned here.
        (UsbTmcDeviceProfile.BulkOutEndpointAddress & EndpointNumberMask).ShouldBe(
            CdcAcmDeviceProfile.BulkOutEndpointAddress & EndpointNumberMask
        );
        (UsbTmcDeviceProfile.BulkInEndpointAddress & EndpointNumberMask).ShouldBe(
            CdcAcmDeviceProfile.BulkInEndpointAddress & EndpointNumberMask
        );
        (UsbTmcDeviceProfile.InterruptInEndpointAddress & EndpointNumberMask).ShouldBe(
            CdcAcmDeviceProfile.InterruptInEndpointAddress & EndpointNumberMask
        );
    }

    [Fact]
    public async Task Devlist_lists_a_cdc_route_as_a_communications_device()
    {
        await using var bench = await UsbIpBench.StartCdcAcmAsync();

        var reply = await bench.Connect().RequestDevlistAsync(bench.Token);

        var exported = reply.Devices.ShouldHaveSingleItem();
        exported.Device.BusId.ShouldBe(UsbIpBench.CdcAcmBusId);
        exported.Device.IdVendor.ShouldBe(UsbIpGatewayServer.MockVendorId);
        exported.Device.IdProduct.ShouldBe(UsbIpGatewayServer.MockCdcAcmProductId);
        exported.Device.IdProduct.ShouldNotBe(UsbIpGatewayServer.MockProductId);
        exported.Device.DeviceClass.ShouldBe(CdcAcmConstants.CommunicationsDeviceClass);
        exported.Device.NumInterfaces.ShouldBe((byte)2);

        exported.Interfaces.Length.ShouldBe(2);
        exported
            .Interfaces[0]
            .InterfaceClass.ShouldBe(CdcAcmConstants.CommunicationsInterfaceClass);
        exported
            .Interfaces[0]
            .InterfaceSubClass.ShouldBe(CdcAcmConstants.AbstractControlModelSubClass);
        exported.Interfaces[0].InterfaceProtocol.ShouldBe(CdcAcmConstants.AtCommandProtocolV250);
        exported.Interfaces[1].InterfaceClass.ShouldBe(CdcAcmConstants.DataInterfaceClass);
    }

    [Fact]
    public async Task An_imported_cdc_device_enumerates_its_configuration_blob()
    {
        await using var bench = await UsbIpBench.StartCdcAcmAsync();
        var client = await bench.ImportAsync(UsbIpBench.CdcAcmBusId);

        var blob = await client.ControlInAsync(
            UsbIpTestClient.DeviceToHostStandardDevice,
            UsbStandardRequest.GetDescriptor,
            wValue: UsbDescriptorType.Configuration << 8,
            wIndex: 0,
            wLength: 255,
            bench.Token
        );

        blob.Reply.Status.ShouldBe(0);
        blob.Payload.Length.ShouldBe(ConfigurationBlobLength);
        blob.Payload[1].ShouldBe((byte)UsbDescriptorType.Configuration);
        BitConverter.ToUInt16(blob.Payload, 2).ShouldBe((ushort)ConfigurationBlobLength);
    }

    [Fact]
    public async Task A_line_coding_the_host_sets_reads_back_unchanged()
    {
        await using var bench = await UsbIpBench.StartCdcAcmAsync();
        var client = await bench.ImportAsync(UsbIpBench.CdcAcmBusId);
        var coding = new CdcLineCoding(
            DteRate: 9600,
            CharFormat: 2,
            ParityType: 2,
            DataBits: 7
        ).ToArray();

        var set = await client.ControlOutAsync(
            UsbIpTestClient.HostToDeviceClassInterface,
            CdcAcmConstants.RequestSetLineCoding,
            wValue: 0,
            wIndex: CdcAcmDeviceProfile.CommunicationsInterfaceNumber,
            coding,
            bench.Token
        );
        set.Reply.Status.ShouldBe(0);
        set.Reply.ActualLength.ShouldBe(LineCodingLength);

        var read = await client.ControlInAsync(
            UsbIpTestClient.DeviceToHostClassInterface,
            CdcAcmConstants.RequestGetLineCoding,
            wValue: 0,
            wIndex: CdcAcmDeviceProfile.CommunicationsInterfaceNumber,
            wLength: LineCodingLength,
            bench.Token
        );

        read.Reply.Status.ShouldBe(0);
        read.Payload.ShouldBe(coding);
    }

    [Fact]
    public async Task A_query_written_as_a_line_answers_over_the_bulk_in_endpoint()
    {
        await using var bench = await UsbIpBench.StartCdcAcmAsync();
        var client = await bench.ImportAsync(UsbIpBench.CdcAcmBusId);

        var written = await client.BulkOutAsync(Line("*IDN?"), bench.Token);
        written.Reply.Status.ShouldBe(0);
        written.Reply.ActualLength.ShouldBe(Line("*IDN?").Length);

        var answer = await client.BulkInAsync(bufferLength: 512, bench.Token);

        answer.Reply.Status.ShouldBe(0);
        Encoding.ASCII.GetString(answer.Payload).ShouldBe(UsbIpBench.IdnResponse + "\n");
    }

    [Fact]
    public async Task A_bulk_in_urb_submitted_before_the_answer_exists_completes_once_it_does()
    {
        await using var bench = await UsbIpBench.StartCdcAcmAsync();
        var client = await bench.ImportAsync(UsbIpBench.CdcAcmBusId);

        // A terminal keeps a read outstanding at all times, so the IN URB
        // is normally already parked when the question goes out.
        var parked = client.SubmitBulkIn(bufferLength: 512);
        var outbound = client.SubmitBulkOut(Line("*IDN?"));
        await client.FlushAsync(bench.Token);

        var first = await client.ReadSubmitReplyAsync(bench.Token);
        var second = await client.ReadSubmitReplyAsync(bench.Token);

        first.Reply.Header.SeqNum.ShouldBe(outbound);
        second.Reply.Header.SeqNum.ShouldBe(parked);
        Encoding.ASCII.GetString(second.Payload).ShouldBe(UsbIpBench.IdnResponse + "\n");
    }

    [Fact]
    public async Task Blank_lines_are_acknowledged_and_answered_by_nothing()
    {
        await using var bench = await UsbIpBench.StartCdcAcmAsync();
        var client = await bench.ImportAsync(UsbIpBench.CdcAcmBusId);

        var parked = client.SubmitBulkIn(bufferLength: 512);
        var blanks = client.SubmitBulkOut(Encoding.ASCII.GetBytes("\n\r\n\n"));
        await client.FlushAsync(bench.Token);

        var ack = await client.ReadSubmitReplyAsync(bench.Token);
        ack.Reply.Header.SeqNum.ShouldBe(blanks);
        ack.Reply.Status.ShouldBe(0);

        // The parked read is still parked: the next reply on the wire is
        // the answer to the query sent after the blank lines, and it is
        // the only thing that ever reaches it.
        await client.BulkOutAsync(Line("*IDN?"), bench.Token);
        var answer = await client.ReadSubmitReplyAsync(bench.Token);

        answer.Reply.Header.SeqNum.ShouldBe(parked);
        Encoding.ASCII.GetString(answer.Payload).ShouldBe(UsbIpBench.IdnResponse + "\n");
        (await bench.Backend.ReadAsync(bench.Device, bench.Token)).ShouldBeOk().ShouldBe("");
    }

    [Fact]
    public async Task A_write_is_acknowledged_and_reaches_the_backend()
    {
        await using var bench = await UsbIpBench.StartCdcAcmAsync();
        var client = await bench.ImportAsync(UsbIpBench.CdcAcmBusId);

        var written = await client.BulkOutAsync(Line(":VOLT 24.000"), bench.Token);

        written.Reply.Status.ShouldBe(0);
        // Acknowledged only after the SCPI reached the backend, so no
        // wait is needed to observe it.
        (await bench.Backend.ReadAsync(bench.Device, bench.Token))
            .ShouldBeOk()
            .ShouldBe(":VOLT 24.000");
    }

    [Fact]
    public async Task A_command_split_across_two_transfers_dispatches_once_the_line_closes()
    {
        await using var bench = await UsbIpBench.StartCdcAcmAsync();
        var client = await bench.ImportAsync(UsbIpBench.CdcAcmBusId);

        await client.BulkOutAsync(Encoding.ASCII.GetBytes(":VOLT "), bench.Token);
        (await bench.Backend.ReadAsync(bench.Device, bench.Token)).ShouldBeOk().ShouldBe("");

        await client.BulkOutAsync(Encoding.ASCII.GetBytes("24.000\r\n"), bench.Token);

        (await bench.Backend.ReadAsync(bench.Device, bench.Token))
            .ShouldBeOk()
            .ShouldBe(":VOLT 24.000");
    }

    [Fact]
    public async Task Unlinking_a_parked_interrupt_urb_answers_ECONNRESET()
    {
        await using var bench = await UsbIpBench.StartCdcAcmAsync();
        var client = await bench.ImportAsync(UsbIpBench.CdcAcmBusId);

        // The notification endpoint exists because CDC 1.1 §3.3.1
        // requires it; nothing is ever queued for it, so a URB submitted
        // to it waits until the host takes it back.
        var interrupt = client.SubmitInterruptIn(bufferLength: 8);
        var unlink = client.SubmitUnlink(interrupt);
        await client.FlushAsync(bench.Token);

        var answer = await client.ReadUnlinkReplyAsync(bench.Token);
        answer.Header.SeqNum.ShouldBe(unlink);
        answer.Status.ShouldBe(UsbIpGatewayServer.UrbUnlinkedStatus);

        var probe = await client.ControlInAsync(
            UsbIpTestClient.DeviceToHostStandardDevice,
            UsbStandardRequest.GetDescriptor,
            wValue: UsbDescriptorType.Device << 8,
            wIndex: 0,
            wLength: 64,
            bench.Token
        );
        probe.Reply.Header.SeqNum.ShouldNotBe(interrupt);
    }

    [Fact]
    public async Task One_server_serves_a_usbtmc_export_and_a_cdc_export_side_by_side()
    {
        await using var bench = await UsbIpBench.StartAsync(
            (UsbIpBench.BusId, UsbExportProfile.UsbTmc),
            (UsbIpBench.CdcAcmBusId, UsbExportProfile.CdcAcm)
        );

        var devlist = await bench.Connect().RequestDevlistAsync(bench.Token);
        devlist.Devices.Length.ShouldBe(2);
        devlist.Devices[0].Device.IdProduct.ShouldBe(UsbIpGatewayServer.MockProductId);
        devlist.Devices[1].Device.IdProduct.ShouldBe(UsbIpGatewayServer.MockCdcAcmProductId);

        var instrument = await bench.ImportAsync(UsbIpBench.BusId);
        await instrument.BulkOutAsync(
            UsbTmcCodec.WriteDevDepMsgOut(
                new UsbTmcDevDepMsgOut(
                    BTag: 1,
                    EndOfMessage: true,
                    Encoding.ASCII.GetBytes("*IDN?\n")
                )
            ),
            bench.Token
        );
        await instrument.BulkOutAsync(
            UsbTmcCodec.WriteRequestDevDepMsgIn(
                new UsbTmcRequestDevDepMsgIn(2, 1024, TermCharEnabled: false, TermChar: 0)
            ),
            bench.Token
        );
        var framed = await instrument.BulkInAsync(bufferLength: 1024, bench.Token);
        Encoding
            .ASCII.GetString(UsbTmcCodec.ReadDevDepMsgIn(framed.Payload).Payload)
            .ShouldBe(UsbIpBench.IdnResponse + "\n");

        var terminal = await bench.ImportAsync(UsbIpBench.CdcAcmBusId);
        await terminal.BulkOutAsync(Line("*IDN?"), bench.Token);
        var plain = await terminal.BulkInAsync(bufferLength: 512, bench.Token);
        Encoding.ASCII.GetString(plain.Payload).ShouldBe(UsbIpBench.IdnResponse + "\n");
    }

    private static byte[] Line(string scpi) => Encoding.ASCII.GetBytes(scpi + "\n");

    private const uint EndpointNumberMask = 0x0F;
}
