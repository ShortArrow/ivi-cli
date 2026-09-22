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
    [InlineData("MEAS:VOLT? (@1)")]
    [InlineData("*IDN? ")]
    public void From_accepts_a_query_with_parameters_or_trailing_whitespace(string text) =>
        ScpiQuery.From(text).ShouldBeOk().Value.ShouldBe(text);

    [Fact]
    public void From_rejects_a_request_without_a_query_header() =>
        ScpiQuery.From("VOLT MAX?").ShouldBeError();
}
