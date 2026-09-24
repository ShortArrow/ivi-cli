using IviCli.Domain.Scpi;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Domain.Tests.Scpi;

/// <summary>
/// On the 0.3.x line a request expects a response when a program header
/// ends in <c>?</c> (IEEE 488.2) or, as before, when the text ends in
/// <c>?</c>. The first rule answers queries with parameters; the second
/// keeps every request 0.3.1 answered answered.
/// </summary>
public sealed class ScpiMessageTests
{
    [Theory]
    [InlineData("*IDN?")]
    [InlineData("*idn?")]
    [InlineData("*IDN?\r\n")]
    [InlineData("*IDN? ")]
    [InlineData("MEAS:VOLT? (@1)")]
    [InlineData("MEAS:VOLT? CH1")]
    [InlineData("MEAS:VOLT?\tCH1")]
    [InlineData("VOLT 1;MEAS:VOLT? (@1)")]
    [InlineData("VOLT 1\n*OPC?")]
    [InlineData("DISP:TEXT \"a;b\";*OPC?")]
    [InlineData("DATA #14ab?c;*OPC?")]
    [InlineData("VOLT MAX?")]
    public void A_request_with_a_query_header_or_a_final_question_mark_expects_a_response(
        string text
    ) => ScpiMessage.ExpectsResponse(text).ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("VOLT 1")]
    [InlineData("*RST")]
    [InlineData("SOUR:VOLT #HFF")]
    [InlineData("DISP:TEXT 'x;y?' ")]
    [InlineData("DISP:TEXT \"ready?\"")]
    [InlineData("DATA #15ab?cd")]
    public void A_request_with_neither_expects_no_response(string text) =>
        ScpiMessage.ExpectsResponse(text).ShouldBeFalse();

    [Theory]
    [InlineData("MEAS:VOLT? (@1)")]
    [InlineData("MEAS:VOLT? CH1")]
    [InlineData("VOLT MAX?")]
    public void From_accepts_what_expects_a_response(string text) =>
        ScpiQuery.From(text).ShouldBeOk().Value.ShouldBe(text);

    [Fact]
    public void From_refuses_what_expects_none() => ScpiQuery.From("*RST").ShouldBeError();
}
