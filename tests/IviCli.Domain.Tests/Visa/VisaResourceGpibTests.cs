using IviCli.Domain.Visa;
using IviCli.TestKit;

namespace IviCli.Domain.Tests.Visa;

public class VisaResourceGpibTests
{
    [Fact]
    public void Parse_GpibPrimaryOnly_ReturnsGpib()
    {
        // Given
        const string raw = "GPIB0::5::INSTR";

        // When
        var result = VisaResource.Parse(raw);

        // Then
        var gpib = result.ShouldBeOk().ShouldBeOfType<VisaResource.Gpib>();
        gpib.Board.ShouldBe(0);
        gpib.PrimaryAddress.ShouldBe(5);
        gpib.SecondaryAddress.ShouldBeNull();
    }

    [Fact]
    public void Parse_GpibWithImplicitBoard_DefaultsBoardToZero()
    {
        // Given
        const string raw = "GPIB::7::INSTR";

        // When
        var result = VisaResource.Parse(raw);

        // Then
        var gpib = result.ShouldBeOk().ShouldBeOfType<VisaResource.Gpib>();
        gpib.Board.ShouldBe(0);
        gpib.PrimaryAddress.ShouldBe(7);
    }

    [Fact]
    public void Parse_GpibWithExplicitBoard_CapturesBoardNumber()
    {
        // Given
        const string raw = "GPIB1::5::INSTR";

        // When
        var result = VisaResource.Parse(raw);

        // Then
        var gpib = result.ShouldBeOk().ShouldBeOfType<VisaResource.Gpib>();
        gpib.Board.ShouldBe(1);
        gpib.PrimaryAddress.ShouldBe(5);
    }

    [Fact]
    public void Parse_GpibWithSecondaryAddress_CapturesBoth()
    {
        // Given
        const string raw = "GPIB0::5::10::INSTR";

        // When
        var result = VisaResource.Parse(raw);

        // Then
        var gpib = result.ShouldBeOk().ShouldBeOfType<VisaResource.Gpib>();
        gpib.PrimaryAddress.ShouldBe(5);
        gpib.SecondaryAddress.ShouldBe(10);
    }

    [Theory]
    [InlineData("GPIB0::5::INSTR", 5)]
    [InlineData("GPIB0::0::INSTR", 0)] // boundary: lowest valid
    [InlineData("GPIB0::30::INSTR", 30)] // boundary: highest valid
    public void Parse_PrimaryAddressInRange_Succeeds(string raw, int expected)
    {
        var gpib = VisaResource.Parse(raw).ShouldBeOk().ShouldBeOfType<VisaResource.Gpib>();
        gpib.PrimaryAddress.ShouldBe(expected);
    }

    [Theory]
    [InlineData("GPIB0::INSTR")] // missing primary address
    [InlineData("GPIB0::5")] // missing suffix
    [InlineData("GPIB0::5::INSTR::EXTRA")] // too many segments
    [InlineData("GPIB0::5::NOTINSTR")] // wrong suffix
    [InlineData("GPIB0::not_a_number::INSTR")] // non-numeric primary
    [InlineData("GPIB0::-1::INSTR")] // negative primary
    [InlineData("GPIB0::31::INSTR")] // primary out of range (>30)
    [InlineData("GPIB0::5::31::INSTR")] // secondary out of range (>30)
    [InlineData("GPIB0::5::not_a_number::INSTR")] // non-numeric secondary
    [InlineData("GPIBx::5::INSTR")] // non-numeric board
    public void Parse_InvalidGpibInput_ReturnsInvalidVisaResourceFormat(string raw)
    {
        var err = VisaResource
            .Parse(raw)
            .ShouldBeError()
            .ShouldBeOfType<InvalidVisaResourceFormat>();
        err.Raw.ShouldBe(raw);
    }

    [Fact]
    public void Gpib_Equality_IsByValue()
    {
        // Given
        var a = VisaResource.Parse("GPIB0::5::INSTR").ShouldBeOk();
        var b = VisaResource.Parse("GPIB0::5::INSTR").ShouldBeOk();
        var c = VisaResource.Parse("GPIB0::6::INSTR").ShouldBeOk();

        // When / Then
        a.ShouldBe(b);
        a.ShouldNotBe(c);
    }
}
