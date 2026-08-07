using System.Text;
using IviCli.Domain.Protocols;

namespace IviCli.Domain.Tests.Protocols;

/// <summary>
/// Behaviour tests for the USBTMC message exchange of ADR 0049 §2 with
/// no transport under it: bulk-OUT transfers in, complete messages and
/// bulk-IN transfers out. The rules — accumulate until EOM, one
/// DEV_DEP_MSG_IN per REQUEST_DEV_DEP_MSG_IN capped at the host's
/// TransferSize, a bTag that never repeats — are USBTMC 1.00 §3.2 and
/// §3.3.
/// </summary>
public sealed class UsbTmcMessagePumpTests
{
    private const uint SmallHostBuffer = 4;

    [Fact]
    public void A_single_transfer_with_EOM_completes_one_message()
    {
        var result = new UsbTmcMessagePump().SubmitBulkOut(UsbTmcGoldenTransfers.DevDepMsgOutIdn);

        result.Outcome.ShouldBe(UsbTmcBulkOutOutcome.MessageComplete);
        result.Message!.Value.BTag.ShouldBe((byte)1);
        result.Message!.Value.Content.ShouldBe(UsbTmcGoldenTransfers.IdnQuery);
    }

    [Fact]
    public void A_message_split_across_two_transfers_completes_only_on_the_second()
    {
        var pump = new UsbTmcMessagePump();

        var first = pump.SubmitBulkOut(Out(bTag: 1, endOfMessage: false, "*ID"));
        first.Outcome.ShouldBe(UsbTmcBulkOutOutcome.Accumulated);
        first.Message.ShouldBeNull();

        var second = pump.SubmitBulkOut(Out(bTag: 2, endOfMessage: true, "N?\n"));

        second.Outcome.ShouldBe(UsbTmcBulkOutOutcome.MessageComplete);
        second.Message!.Value.Content.ShouldBe(UsbTmcGoldenTransfers.IdnQuery);

        // The message is named by the bTag that opened it.
        second.Message!.Value.BTag.ShouldBe((byte)1);
    }

    [Fact]
    public void A_bTag_reused_by_the_next_header_is_rejected()
    {
        var pump = new UsbTmcMessagePump();
        pump.SubmitBulkOut(Out(bTag: 1, endOfMessage: true, "*IDN?\n"));

        var result = pump.SubmitBulkOut(Out(bTag: 1, endOfMessage: true, "*RST\n"));

        result.Outcome.ShouldBe(UsbTmcBulkOutOutcome.Rejected);
        result.Message.ShouldBeNull();
    }

    [Fact]
    public void A_bTag_that_only_differs_from_the_previous_one_is_accepted()
    {
        var pump = new UsbTmcMessagePump();
        pump.SubmitBulkOut(Out(bTag: 1, endOfMessage: true, "*RST\n"));
        pump.SubmitBulkOut(Out(bTag: 2, endOfMessage: true, "*CLS\n"));

        // Back to 1 is legal: the rule forbids repeating the immediately
        // preceding bTag, not ever reusing a value.
        var result = pump.SubmitBulkOut(Out(bTag: 1, endOfMessage: true, "*IDN?\n"));

        result.Outcome.ShouldBe(UsbTmcBulkOutOutcome.MessageComplete);
    }

    [Fact]
    public void A_broken_bTagInverse_is_rejected_rather_than_thrown_at_the_caller()
    {
        var transfer = UsbTmcGoldenTransfers.DevDepMsgOutIdn;
        transfer[2] = 0x01;

        var result = new UsbTmcMessagePump().SubmitBulkOut(transfer);

        result.Outcome.ShouldBe(UsbTmcBulkOutOutcome.Rejected);
        result.Reason.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void A_MsgID_the_profile_does_not_speak_is_rejected()
    {
        var transfer = UsbTmcGoldenTransfers.DevDepMsgOutIdn;
        transfer[0] = 126; // VENDOR_SPECIFIC_OUT, out of scope

        var result = new UsbTmcMessagePump().SubmitBulkOut(transfer);

        result.Outcome.ShouldBe(UsbTmcBulkOutOutcome.Rejected);
    }

    [Fact]
    public void A_rejected_transfer_leaves_the_accumulated_message_untouched()
    {
        var pump = new UsbTmcMessagePump();
        pump.SubmitBulkOut(Out(bTag: 1, endOfMessage: false, "*ID"));

        var broken = UsbTmcGoldenTransfers.DevDepMsgOutIdn;
        broken[2] = 0x01;
        pump.SubmitBulkOut(broken).Outcome.ShouldBe(UsbTmcBulkOutOutcome.Rejected);

        var result = pump.SubmitBulkOut(Out(bTag: 2, endOfMessage: true, "N?\n"));
        result.Message!.Value.Content.ShouldBe(UsbTmcGoldenTransfers.IdnQuery);
    }

    [Fact]
    public void REQUEST_DEV_DEP_MSG_IN_arms_a_pending_in_transfer()
    {
        var pump = new UsbTmcMessagePump();
        pump.PendingIn.ShouldBeNull();

        var result = pump.SubmitBulkOut(UsbTmcGoldenTransfers.RequestDevDepMsgIn);

        result.Outcome.ShouldBe(UsbTmcBulkOutOutcome.InRequested);
        pump.PendingIn!.Value.BTag.ShouldBe((byte)2);
        pump.PendingIn!.Value.MaxTransferSize.ShouldBe(1024u);
    }

    [Fact]
    public void A_REQUEST_DEV_DEP_MSG_IN_asking_for_nothing_is_rejected()
    {
        var request = UsbTmcCodec.WriteRequestDevDepMsgIn(
            new UsbTmcRequestDevDepMsgIn(
                BTag: 3,
                TransferSize: 0,
                TermCharEnabled: false,
                TermChar: 0
            )
        );

        var result = new UsbTmcMessagePump().SubmitBulkOut(request);

        result.Outcome.ShouldBe(UsbTmcBulkOutOutcome.Rejected);
    }

    [Fact]
    public void A_response_that_fits_becomes_one_DEV_DEP_MSG_IN_with_EOM()
    {
        var pump = new UsbTmcMessagePump();
        pump.SubmitBulkOut(UsbTmcGoldenTransfers.RequestDevDepMsgIn);
        pump.SupplyResponse(UsbTmcGoldenTransfers.MockAnswer);

        pump.TryTakeBulkIn(out var transfer).ShouldBeTrue();
        transfer.ShouldBe(UsbTmcGoldenTransfers.DevDepMsgInMock);

        // The exchange is over: nothing more to hand the host.
        pump.TryTakeBulkIn(out _).ShouldBeFalse();
        pump.PendingIn.ShouldBeNull();
    }

    [Fact]
    public void A_response_larger_than_the_host_buffer_is_split_with_EOM_on_the_last_transfer()
    {
        var pump = new UsbTmcMessagePump();
        pump.SubmitBulkOut(
            UsbTmcCodec.WriteRequestDevDepMsgIn(
                new UsbTmcRequestDevDepMsgIn(
                    BTag: 9,
                    TransferSize: SmallHostBuffer,
                    TermCharEnabled: false,
                    TermChar: 0
                )
            )
        );
        pump.SupplyResponse(Encoding.ASCII.GetBytes("MOCK\n"));

        pump.TryTakeBulkIn(out var first).ShouldBeTrue();
        var firstMessage = UsbTmcCodec.ReadDevDepMsgIn(first);
        firstMessage.BTag.ShouldBe((byte)9);
        firstMessage.TransferSize.ShouldBe(SmallHostBuffer);
        firstMessage.Payload.ShouldBe(Encoding.ASCII.GetBytes("MOCK"));
        firstMessage.EndOfMessage.ShouldBeFalse();

        pump.TryTakeBulkIn(out var second).ShouldBeTrue();
        var secondMessage = UsbTmcCodec.ReadDevDepMsgIn(second);
        secondMessage.BTag.ShouldBe((byte)9);
        secondMessage.Payload.ShouldBe(Encoding.ASCII.GetBytes("\n"));
        secondMessage.EndOfMessage.ShouldBeTrue();

        pump.TryTakeBulkIn(out _).ShouldBeFalse();
    }

    [Fact]
    public void A_second_request_takes_over_the_bTag_of_the_remaining_transfers()
    {
        var pump = new UsbTmcMessagePump();
        pump.SubmitBulkOut(
            UsbTmcCodec.WriteRequestDevDepMsgIn(
                new UsbTmcRequestDevDepMsgIn(9, SmallHostBuffer, false, 0)
            )
        );
        pump.SupplyResponse(Encoding.ASCII.GetBytes("MOCK\n"));
        pump.TryTakeBulkIn(out _).ShouldBeTrue();

        // The host asks for the rest under a fresh bTag, as USBTMC
        // requires of every new Bulk-OUT header.
        pump.SubmitBulkOut(
                UsbTmcCodec.WriteRequestDevDepMsgIn(
                    new UsbTmcRequestDevDepMsgIn(10, SmallHostBuffer, false, 0)
                )
            )
            .Outcome.ShouldBe(UsbTmcBulkOutOutcome.InRequested);

        pump.TryTakeBulkIn(out var rest).ShouldBeTrue();
        var restMessage = UsbTmcCodec.ReadDevDepMsgIn(rest);
        restMessage.BTag.ShouldBe((byte)10);
        restMessage.Payload.ShouldBe(Encoding.ASCII.GetBytes("\n"));
        restMessage.EndOfMessage.ShouldBeTrue();
    }

    [Fact]
    public void An_empty_response_is_still_one_transfer_carrying_EOM()
    {
        var pump = new UsbTmcMessagePump();
        pump.SubmitBulkOut(UsbTmcGoldenTransfers.RequestDevDepMsgIn);
        pump.SupplyResponse([]);

        pump.TryTakeBulkIn(out var transfer).ShouldBeTrue();
        var message = UsbTmcCodec.ReadDevDepMsgIn(transfer);
        message.TransferSize.ShouldBe(0u);
        message.EndOfMessage.ShouldBeTrue();
        transfer.Length.ShouldBe(UsbTmcConstants.BulkHeaderSize);
    }

    [Fact]
    public void A_response_supplied_before_the_host_asks_waits_for_the_request()
    {
        var pump = new UsbTmcMessagePump();
        pump.SupplyResponse(UsbTmcGoldenTransfers.MockAnswer);

        pump.TryTakeBulkIn(out _).ShouldBeFalse();

        pump.SubmitBulkOut(UsbTmcGoldenTransfers.RequestDevDepMsgIn);
        pump.TryTakeBulkIn(out var transfer).ShouldBeTrue();
        transfer.ShouldBe(UsbTmcGoldenTransfers.DevDepMsgInMock);
    }

    [Fact]
    public void Nothing_is_taken_from_a_pump_that_was_never_asked()
    {
        new UsbTmcMessagePump().TryTakeBulkIn(out var transfer).ShouldBeFalse();
        transfer.ShouldBeEmpty();
    }

    [Fact]
    public void Clear_drops_the_partial_message_the_pump_was_accumulating()
    {
        var pump = new UsbTmcMessagePump();
        pump.SubmitBulkOut(Out(bTag: 1, endOfMessage: false, "*ID"));

        pump.Clear();

        var result = pump.SubmitBulkOut(Out(bTag: 2, endOfMessage: true, "*RST\n"));
        result.Outcome.ShouldBe(UsbTmcBulkOutOutcome.MessageComplete);
        result.Message!.Value.Content.ShouldBe(Encoding.ASCII.GetBytes("*RST\n"));
        result.Message!.Value.BTag.ShouldBe((byte)2);
    }

    [Fact]
    public void Clear_drops_a_pending_in_transfer_and_its_response()
    {
        var pump = new UsbTmcMessagePump();
        pump.SubmitBulkOut(UsbTmcGoldenTransfers.RequestDevDepMsgIn);
        pump.SupplyResponse(UsbTmcGoldenTransfers.MockAnswer);

        pump.Clear();

        pump.PendingIn.ShouldBeNull();
        pump.TryTakeBulkIn(out _).ShouldBeFalse();
    }

    [Fact]
    public void Clear_forgets_the_last_bTag_so_the_host_may_restart_at_one()
    {
        var pump = new UsbTmcMessagePump();
        pump.SubmitBulkOut(Out(bTag: 1, endOfMessage: true, "*RST\n"));

        pump.Clear();

        pump.SubmitBulkOut(Out(bTag: 1, endOfMessage: true, "*IDN?\n"))
            .Outcome.ShouldBe(UsbTmcBulkOutOutcome.MessageComplete);
    }

    private static byte[] Out(byte bTag, bool endOfMessage, string content) =>
        UsbTmcCodec.WriteDevDepMsgOut(
            new UsbTmcDevDepMsgOut(bTag, endOfMessage, Encoding.ASCII.GetBytes(content))
        );
}
