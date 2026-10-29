using IviCli.Domain.Scpi;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Domain.Tests.Scpi;

/// <summary>
/// A request is a query when the header of any of its program message units
/// ends in <c>?</c> (IEEE 488.2), not when the line's last character does.
/// </summary>
public sealed class ScpiMessageTests
{
    [Theory]
    [InlineData("25.3,72.1", "25.3|72.1")]
    [InlineData(" 1 , 2 ", "1|2")]
    [InlineData("1;2,3", "1;2|3")]
    [InlineData("\"a,b\",1", "\"a,b\"|1")]
    [InlineData("'x;y';1", "'x;y';1")]
    [InlineData("#13a,b,1", "[#13a,b]|1")]
    [InlineData("1,#0a,b;c", "1|[#0a,b;c]")]
    [InlineData("", "")]
    public void A_response_splits_into_units_of_elements_outside_strings_and_blocks(
        string response,
        string expected
    )
    {
        var rendered = string.Join(
            ";",
            ScpiMessage
                .SplitResponse(response)
                .Select(unit =>
                    string.Join("|", unit.Select(e => e.IsBlock ? $"[{e.Text}]" : e.Text))
                )
        );

        rendered.ShouldBe(expected);
    }

    [Theory]
    [InlineData("*IDN?")]
    [InlineData("*idn?")]
    [InlineData("*IDN?\r\n")]
    [InlineData("*IDN? ")]
    [InlineData("SYST:ERR?\t")]
    [InlineData("  *IDN?")]
    [InlineData("MEAS:VOLT? (@1)")]
    [InlineData("MEAS:VOLT? CH1")]
    [InlineData("MEAS:VOLT?\tCH1")]
    [InlineData("MEAS:VOLT?;VOLT 1")]
    [InlineData("VOLT 1;MEAS:VOLT? (@1)")]
    [InlineData("VOLT 1; MEAS:VOLT?")]
    [InlineData("VOLT 1\n*OPC?")]
    [InlineData("VOLT 1\r\n*OPC?\r\n")]
    [InlineData("DISP:TEXT \"a;b\";*OPC?")]
    [InlineData("DISP:TEXT 'it''s';*OPC?")]
    [InlineData("DATA #14ab?c;*OPC?")]
    public void A_header_ending_in_a_question_mark_makes_a_query(string text) =>
        ScpiMessage.IsQuery(text).ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("VOLT 1")]
    [InlineData("SOUR:VOLT #HFF")]
    [InlineData("VOLT MAX?")]
    [InlineData("DISP:TEXT \"ready?\"")]
    [InlineData("DISP:TEXT 'x;y?'")]
    [InlineData("DATA #15ab?cd")]
    [InlineData("DATA #0ab?;*OPC?")]
    [InlineData("DATA #9123")]
    public void No_header_ending_in_a_question_mark_makes_a_write(string text) =>
        ScpiMessage.IsQuery(text).ShouldBeFalse();

    [Theory]
    [InlineData("*IDN? ", "*IDN?")]
    [InlineData("*IDN?\r\n", "*IDN?")]
    [InlineData("SYST:ERR?\t\n", "SYST:ERR?")]
    [InlineData("VOLT 1 ;*OPC? \r\n", "VOLT 1 ;*OPC?")]
    [InlineData("DISP:TEXT \"a \" ", "DISP:TEXT \"a \"")]
    [InlineData("DATA #13ab ", "DATA #13ab ")]
    [InlineData("DATA #13ab \r\n", "DATA #13ab ")]
    [InlineData("DATA #12a\n", "DATA #12a\n")]
    [InlineData("DATA #12a\r", "DATA #12a\r")]
    [InlineData("DATA #0ab \n", "DATA #0ab ")]
    [InlineData("DATA #0ab ", "DATA #0ab ")]
    [InlineData("DATA #0ab\r\n", "DATA #0ab\r")]
    [InlineData("DATA #15ab\r\n", "DATA #15ab\r\n")]
    [InlineData("DISP:TEXT \"ab \r\n", "DISP:TEXT \"ab \r\n")]
    [InlineData("DATA #2 3a  ", "DATA #2 3a")]
    [InlineData("DATA #2+3a  ", "DATA #2+3a")]
    [InlineData("   ", "")]
    [InlineData("", "")]
    public void TrimEnd_drops_trailing_whitespace_outside_block_data(
        string text,
        string expected
    ) => ScpiMessage.TrimEnd(text).ShouldBe(expected);

    [Theory]
    [InlineData("MEAS:VOLT? (@1)")]
    [InlineData("*IDN? ")]
    public void From_accepts_a_query_with_parameters_or_trailing_whitespace(string text) =>
        ScpiQuery.From(text).ShouldBeOk().Value.ShouldBe(text);

    [Theory]
    [InlineData("VOLT MAX?")]
    [InlineData("VOLT MAX?\r\n")]
    public void From_accepts_a_text_ending_in_a_question_mark_even_without_a_query_header(
        string text
    ) => ScpiQuery.From(text).ShouldBeOk().Value.ShouldBe(text);

    [Theory]
    [InlineData("meas:volt?", "MEAS:VOLT?")]
    [InlineData("Outp on", "OUTP ON")]
    [InlineData("DISP:TEXT \"Ready\"", "DISP:TEXT \"Ready\"")]
    [InlineData("disp:text 'it''s'", "DISP:TEXT 'it''s'")]
    [InlineData("data #13abc;*opc?", "DATA #13abc;*OPC?")]
    public void FoldCase_upper_cases_everything_but_quoted_strings_and_block_data(
        string text,
        string folded
    ) => ScpiMessage.FoldCase(text).ShouldBe(folded);

    [Theory]
    [InlineData("*RST")]
    [InlineData("VOLT 1")]
    [InlineData("DISP:TEXT \"ready?\"")]
    public void From_rejects_a_text_with_neither_a_query_header_nor_a_final_question_mark(
        string text
    ) => ScpiQuery.From(text).ShouldBeError();
}
