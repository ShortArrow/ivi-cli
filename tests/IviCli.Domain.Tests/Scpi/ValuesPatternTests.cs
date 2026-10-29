using IviCli.Domain.Scpi;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Domain.Tests.Scpi;

/// <summary>
/// The <c>!values</c> pattern language (ADR 0051 §5): slots separated by
/// <c>,</c> and <c>;</c> as the response is, each a literal, <c>*</c> or a
/// numeric predicate.
/// </summary>
public sealed class ValuesPatternTests
{
    [Theory]
    [InlineData("{20..30},{40..60}", "25.3,72.1", "slot 2 is 72.1, not in 40..60")]
    [InlineData("{temp:20..30},{rh:40..60}", "25.3,72.1", "slot 2 (rh) is 72.1, not in 40..60")]
    [InlineData("{>0}", "0", "slot 1 is 0, not > 0")]
    [InlineData("{>=1}", "0.5", "slot 1 is 0.5, not >= 1")]
    [InlineData("{<0}", "0", "slot 1 is 0, not < 0")]
    [InlineData("{<=1}", "1.5", "slot 1 is 1.5, not <= 1")]
    [InlineData("ON", "OFF", "slot 1 is OFF, not ON")]
    [InlineData("ON", "on", "slot 1 is on, not ON")]
    [InlineData("{v:1..2}", "ON", "slot 1 (v) is not a number: ON")]
    [InlineData("{1..2}", "#13a,b", "slot 1 is block data, which only * matches")]
    [InlineData("#13a,b", "#13a,b", "slot 1 is block data, which only * matches")]
    [InlineData("{1..2},{1..2}", "1.5", "the response has 1 slot, the pattern 2")]
    [InlineData("{1..2}", "1.5,1.5", "the response has 2 slots, the pattern 1")]
    [InlineData("1;2", "1", "the response has 1 unit, the pattern 2")]
    [InlineData("1;2,3", "1;2", "unit 2 of the response has 1 slot, the pattern 2")]
    [InlineData("{1..2}", "Infinity", "slot 1 is not a number: Infinity")]
    public void A_response_the_pattern_refuses_is_named_by_its_slot(
        string pattern,
        string response,
        string reason
    ) => Parse(pattern).Match(response).ShouldBe(reason);

    [Theory]
    [InlineData("{20..30},{40..60}", "25.3,52.1")]
    [InlineData("{temp:20..30},{rh:40..60}", "25.3,52.1")]
    [InlineData("{20..30}", "20")]
    [InlineData("{20..30}", "30")]
    [InlineData("{>0}", "1E-9")]
    [InlineData("{>=1}", "1")]
    [InlineData("{<0}", "-0.567")]
    [InlineData("{<=1}", "1")]
    [InlineData("{1.2..1.3},{-0.6..-0.5},{3..3}", "1.234,-0.567,3.000")]
    [InlineData("{>9E37}", "9.9E+37")]
    [InlineData("{0..10}", "+5")]
    [InlineData("{0..10}", " 5 ")]
    [InlineData("ON", " ON ")]
    [InlineData("1,\"No error\"", "1,\"No error\"")]
    [InlineData("0,\"a, b\"", "0,\"a, b\"")]
    [InlineData("*,{>0}", "whatever,2")]
    [InlineData("*", "#13a,b")]
    [InlineData("*,1", "#13a,b,1")]
    [InlineData("{0..1};{2..3},*", "0.5;2.5,x")]
    public void A_response_the_pattern_accepts_matches(string pattern, string response) =>
        Parse(pattern).Match(response).ShouldBeNull();

    [Theory]
    [InlineData("", "the pattern is empty")]
    [InlineData("1,,2", "slot 2 is empty")]
    [InlineData(
        "{==3.271}",
        "slot 1: equality is not a predicate; write a range such as 3.27..3.28"
    )]
    [InlineData(
        "{=3.271}",
        "slot 1: equality is not a predicate; write a range such as 3.27..3.28"
    )]
    [InlineData("{3.271}", "slot 1: expected >, >=, <, <= or a range lo..hi")]
    [InlineData("{30..20}", "slot 1: the range 30..20 is empty")]
    [InlineData("{>x}", "slot 1: x is not a number")]
    [InlineData("{1..y}", "slot 1: y is not a number")]
    [InlineData("{2t:1..2}", "slot 1: 2t is not a slot name")]
    [InlineData("{1..2", "slot 1: { is not closed")]
    public void A_malformed_pattern_is_refused_with_the_reason(string pattern, string reason) =>
        ValuesPattern
            .Parse(pattern)
            .ShouldBeError()
            .ShouldBeOfType<InvalidValuesPattern>()
            .Reason.ShouldBe(reason);

    private static ValuesPattern Parse(string pattern) => ValuesPattern.Parse(pattern).ShouldBeOk();
}
