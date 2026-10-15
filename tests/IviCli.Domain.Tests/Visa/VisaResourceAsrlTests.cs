using IviCli.Domain.Visa;
using IviCli.TestKit;

namespace IviCli.Domain.Tests.Visa;

/// <summary>
/// ASRL resources come in two forms (ADR 0055 §1): a board number, which a
/// registration or a VISA runtime maps to a port, and a port name.
/// </summary>
public class VisaResourceAsrlTests
{
    [Theory]
    [InlineData("ASRL1::INSTR", 1)]
    [InlineData("ASRL12::INSTR", 12)]
    [InlineData("ASRL::INSTR", 0)]
    public void Parse_DigitsOnly_ReturnsBoard(string raw, int board)
    {
        // When
        var result = VisaResource.Parse(raw);

        // Then
        result.ShouldBeOk().ShouldBeOfType<VisaResource.AsrlBoard>().Board.ShouldBe(board);
    }

    [Theory]
    [InlineData("ASRL/dev/ttyUSB0::INSTR", "/dev/ttyUSB0")]
    [InlineData("ASRLCOM3::INSTR", "COM3")]
    [InlineData(
        "ASRL/dev/serial/by-id/usb-FTDI_FT232R_A1B2C3-if00-port0::INSTR",
        "/dev/serial/by-id/usb-FTDI_FT232R_A1B2C3-if00-port0"
    )]
    public void Parse_PortName_ReturnsPort(string raw, string port)
    {
        // When
        var result = VisaResource.Parse(raw);

        // Then
        result.ShouldBeOk().ShouldBeOfType<VisaResource.AsrlPort>().Port.ShouldBe(port);
    }

    [Theory]
    [InlineData("ASRL1")] // no class
    [InlineData("ASRL1::SOCKET")] // wrong class
    [InlineData("ASRL1::5::INSTR")] // extra segment
    [InlineData("ASRLCOM3::instr")] // class is case-sensitive, as for the other kinds
    [InlineData("ASRL99999999999::INSTR")] // board past int
    public void Parse_Malformed_ReturnsInvalidFormat(string raw)
    {
        // When
        var result = VisaResource.Parse(raw);

        // Then
        result.ShouldBeError().ShouldBeOfType<InvalidVisaResourceFormat>().Raw.ShouldBe(raw);
    }

    [Theory]
    [InlineData("ASRL1::INSTR")]
    [InlineData("ASRL0::INSTR")]
    [InlineData("ASRL/dev/ttyUSB0::INSTR")]
    [InlineData("ASRLCOM3::INSTR")]
    public void ToCanonical_RoundTrips(string raw)
    {
        // When
        var canonical = VisaResource.Parse(raw).ShouldBeOk().ToCanonical();

        // Then
        canonical.ShouldBe(raw);
    }

    [Fact]
    public void ToCanonical_SpellsTheImplicitBoard()
    {
        // When
        var canonical = VisaResource.Parse("ASRL::INSTR").ShouldBeOk().ToCanonical();

        // Then
        canonical.ShouldBe("ASRL0::INSTR");
    }

    [Fact]
    public void Port_ToLogString_MasksThePortName()
    {
        // Given
        var resource = VisaResource
            .Parse("ASRL/dev/serial/by-id/usb-FTDI_FT232R_A1B2C3-if00-port0::INSTR")
            .ShouldBeOk();

        // When
        var logged = resource.ToLogString();

        // Then
        logged.ShouldBe("ASRL***::INSTR");
    }

    [Fact]
    public void Board_ToLogString_LeavesTheBoardVisible()
    {
        // When
        var logged = VisaResource.Parse("ASRL1::INSTR").ShouldBeOk().ToLogString();

        // Then
        logged.ShouldBe("ASRL1::INSTR");
    }
}
