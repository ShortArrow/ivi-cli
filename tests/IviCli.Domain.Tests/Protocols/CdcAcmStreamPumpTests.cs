using System.Text;
using IviCli.Domain.Protocols;

namespace IviCli.Domain.Tests.Protocols;

/// <summary>
/// Behaviour tests for the CDC data-stream exchange of ADR 0049 §5. A CDC
/// data pipe carries no framing of its own, so the only structure is the
/// newline the SOCKET gateway already uses to frame SCPI lines; these
/// tests pin that the two agree, including on the carriage return a
/// terminal sends before it.
/// </summary>
public sealed class CdcAcmStreamPumpTests
{
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static string Text(byte[] line) => Encoding.UTF8.GetString(line);

    [Fact]
    public void A_transfer_ending_in_a_newline_closes_one_line()
    {
        var pump = new CdcAcmStreamPump();

        var lines = pump.SubmitBulkOut(Bytes("*IDN?\n"));

        Text(lines.ShouldHaveSingleItem()).ShouldBe("*IDN?");
    }

    [Fact]
    public void A_carriage_return_before_the_newline_is_stripped_with_it()
    {
        // A serial terminal sends CRLF; the SOCKET gateway trims the CR
        // and this must agree, or every command reaches the engine with a
        // trailing control character.
        var pump = new CdcAcmStreamPump();

        var lines = pump.SubmitBulkOut(Bytes("*IDN?\r\n"));

        Text(lines.ShouldHaveSingleItem()).ShouldBe("*IDN?");
    }

    [Fact]
    public void One_transfer_can_close_several_lines()
    {
        var pump = new CdcAcmStreamPump();

        var lines = pump.SubmitBulkOut(Bytes("*CLS\n*IDN?\r\nMEAS:VOLT?\n"));

        lines.Count.ShouldBe(3);
        Text(lines[0]).ShouldBe("*CLS");
        Text(lines[1]).ShouldBe("*IDN?");
        Text(lines[2]).ShouldBe("MEAS:VOLT?");
    }

    [Fact]
    public void A_line_split_across_transfers_closes_on_the_transfer_that_ends_it()
    {
        var pump = new CdcAcmStreamPump();

        pump.SubmitBulkOut(Bytes("*ID")).ShouldBeEmpty();
        pump.IsAccumulating.ShouldBeTrue();

        var lines = pump.SubmitBulkOut(Bytes("N?\n"));

        Text(lines.ShouldHaveSingleItem()).ShouldBe("*IDN?");
        pump.IsAccumulating.ShouldBeFalse();
    }

    [Fact]
    public void Bytes_after_the_last_newline_wait_for_the_transfer_that_closes_them()
    {
        var pump = new CdcAcmStreamPump();

        var lines = pump.SubmitBulkOut(Bytes("*CLS\nMEAS"));

        Text(lines.ShouldHaveSingleItem()).ShouldBe("*CLS");
        pump.IsAccumulating.ShouldBeTrue();
        Text(pump.SubmitBulkOut(Bytes(":VOLT?\n")).ShouldHaveSingleItem()).ShouldBe("MEAS:VOLT?");
    }

    [Fact]
    public void A_bare_newline_closes_an_empty_line()
    {
        // Framing only: what an empty line means is the caller's, and the
        // SOCKET gateway drops it before dispatch.
        var pump = new CdcAcmStreamPump();

        var lines = pump.SubmitBulkOut(Bytes("\n"));

        lines.ShouldHaveSingleItem().ShouldBeEmpty();
    }

    [Fact]
    public void An_empty_transfer_closes_nothing()
    {
        var pump = new CdcAcmStreamPump();

        pump.SubmitBulkOut([]).ShouldBeEmpty();
        pump.IsAccumulating.ShouldBeFalse();
    }

    [Fact]
    public void TryTakeBulkIn_hands_back_what_was_supplied()
    {
        var pump = new CdcAcmStreamPump();
        pump.SupplyResponse(Bytes("IVI,MOCK,0,1.0\n"));

        pump.TryTakeBulkIn(512, out var chunk).ShouldBeTrue();

        Text(chunk).ShouldBe("IVI,MOCK,0,1.0\n");
    }

    [Fact]
    public void TryTakeBulkIn_yields_at_most_the_bytes_the_urb_can_hold()
    {
        // A bulk-IN URB completes with whatever exists; the stream has no
        // end-of-message to wait for.
        var pump = new CdcAcmStreamPump();
        pump.SupplyResponse(Bytes("ABCDEFG"));

        pump.TryTakeBulkIn(3, out var first).ShouldBeTrue();
        pump.TryTakeBulkIn(3, out var second).ShouldBeTrue();
        pump.TryTakeBulkIn(3, out var third).ShouldBeTrue();

        Text(first).ShouldBe("ABC");
        Text(second).ShouldBe("DEF");
        Text(third).ShouldBe("G");
        pump.TryTakeBulkIn(3, out var fourth).ShouldBeFalse();
        fourth.ShouldBeEmpty();
    }

    [Fact]
    public void Responses_queue_in_the_order_they_were_supplied()
    {
        var pump = new CdcAcmStreamPump();
        pump.SupplyResponse(Bytes("first\n"));
        pump.SupplyResponse(Bytes("second\n"));

        pump.TryTakeBulkIn(512, out var chunk).ShouldBeTrue();

        Text(chunk).ShouldBe("first\nsecond\n");
    }

    [Fact]
    public void TryTakeBulkIn_reports_nothing_to_send_when_no_response_was_supplied()
    {
        var pump = new CdcAcmStreamPump();

        pump.HasPendingResponse.ShouldBeFalse();
        pump.TryTakeBulkIn(512, out var chunk).ShouldBeFalse();
        chunk.ShouldBeEmpty();
    }

    [Fact]
    public void TryTakeBulkIn_refuses_a_negative_transfer_size()
    {
        var pump = new CdcAcmStreamPump();

        Should.Throw<ArgumentOutOfRangeException>(() => pump.TryTakeBulkIn(-1, out _));
    }

    [Fact]
    public void Clear_drops_the_half_line_and_the_queued_response()
    {
        var pump = new CdcAcmStreamPump();
        pump.SubmitBulkOut(Bytes("MEAS"));
        pump.SupplyResponse(Bytes("stale\n"));

        pump.Clear();

        pump.IsAccumulating.ShouldBeFalse();
        pump.HasPendingResponse.ShouldBeFalse();
        pump.TryTakeBulkIn(512, out _).ShouldBeFalse();
        Text(pump.SubmitBulkOut(Bytes(":VOLT?\n")).ShouldHaveSingleItem()).ShouldBe(":VOLT?");
    }

    [Fact]
    public void The_line_bytes_are_whatever_arrived_between_the_terminators()
    {
        // No SCPI, no encoding: a line is bytes, and a query is only a
        // query to the layer above.
        var pump = new CdcAcmStreamPump();

        var lines = pump.SubmitBulkOut([0x01, 0x02, (byte)'\n']);

        lines.ShouldHaveSingleItem().ShouldBe([0x01, 0x02]);
    }
}
