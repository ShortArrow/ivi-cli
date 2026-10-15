using IviCli.Domain.Devices;
using IviCli.TestKit;

namespace IviCli.Domain.Tests.Devices;

/// <summary>A terminator is LF, CR or CRLF (ADR 0055 §5).</summary>
public class TerminationTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public void From_LfCrOrCrLf_ReturnsTheTerminator(string raw)
    {
        // When
        var result = Termination.From(raw);

        // Then
        result.ShouldBeOk().Value.ShouldBe(raw);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n\r")]
    [InlineData(";")]
    [InlineData("\r\n\r")]
    public void From_AnythingElse_ReturnsInvalidTermination(string raw)
    {
        // When
        var result = Termination.From(raw);

        // Then
        result.ShouldBeError().ShouldBeOfType<InvalidTermination>().Raw.ShouldBe(raw);
    }
}
