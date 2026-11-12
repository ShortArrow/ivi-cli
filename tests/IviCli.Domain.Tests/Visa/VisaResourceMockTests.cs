using IviCli.Domain.Visa;
using IviCli.TestKit;

namespace IviCli.Domain.Tests.Visa;

/// <summary>
/// <c>MOCK::INSTR</c> is a resource with no transport: a device with it
/// answers only from a bound scenario (ADR 0056 §2).
/// </summary>
public class VisaResourceMockTests
{
    [Fact]
    public void Parse_MockInstr_ReturnsMock()
    {
        VisaResource.Parse("MOCK::INSTR").ShouldBeOk().ShouldBeOfType<VisaResource.Mock>();
    }

    [Theory]
    [InlineData("MOCK")] // no class
    [InlineData("MOCK::SOCKET")] // wrong class
    [InlineData("MOCK::instr")] // class is case-sensitive, as for the other kinds
    [InlineData("MOCK0::INSTR")] // no board
    [InlineData("MOCK::sine::INSTR")] // no scenario segment
    [InlineData("mock::INSTR")] // kind is case-sensitive
    public void Parse_Malformed_ReturnsInvalidFormat(string raw)
    {
        VisaResource
            .Parse(raw)
            .ShouldBeError()
            .ShouldBeOfType<InvalidVisaResourceFormat>()
            .Raw.ShouldBe(raw);
    }

    [Fact]
    public void Canonical_and_log_forms_are_the_resource_string()
    {
        var mock = VisaResource.Parse("MOCK::INSTR").ShouldBeOk();

        mock.ToCanonical().ShouldBe("MOCK::INSTR");
        mock.ToLogString().ShouldBe("MOCK::INSTR");
    }
}
