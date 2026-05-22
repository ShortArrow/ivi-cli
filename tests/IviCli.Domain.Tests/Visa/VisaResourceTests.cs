using IviCli.Domain.Visa;
using IviCli.TestKit;

namespace IviCli.Domain.Tests.Visa;

public class VisaResourceTests
{
    [Fact]
    public void Parse_FullTcpipResource_ReturnsTcpip()
    {
        // Given
        const string raw = "TCPIP0::192.168.0.10::inst0::INSTR";

        // When
        var result = VisaResource.Parse(raw);

        // Then
        var tcpip = result.ShouldBeOk().ShouldBeOfType<VisaResource.Tcpip>();
        tcpip.Board.ShouldBe(0);
        tcpip.Host.ShouldBe("192.168.0.10");
        tcpip.LanDevice.ShouldBe("inst0");
    }

    [Fact]
    public void Parse_TcpipWithImplicitBoard_DefaultsBoardToZero()
    {
        // Given
        const string raw = "TCPIP::192.168.0.10::inst0::INSTR";

        // When
        var result = VisaResource.Parse(raw);

        // Then
        var tcpip = result.ShouldBeOk().ShouldBeOfType<VisaResource.Tcpip>();
        tcpip.Board.ShouldBe(0);
    }

    [Fact]
    public void Parse_TcpipWithNonZeroBoard_CapturesBoardNumber()
    {
        // Given
        const string raw = "TCPIP3::host.example.com::hislip0::INSTR";

        // When
        var result = VisaResource.Parse(raw);

        // Then
        var tcpip = result.ShouldBeOk().ShouldBeOfType<VisaResource.Tcpip>();
        tcpip.Board.ShouldBe(3);
        tcpip.Host.ShouldBe("host.example.com");
        tcpip.LanDevice.ShouldBe("hislip0");
    }

    [Fact]
    public void Parse_TcpipWithoutLanDevice_DefaultsToInst0()
    {
        // Given
        const string raw = "TCPIP0::192.168.0.10::INSTR";

        // When
        var result = VisaResource.Parse(raw);

        // Then
        var tcpip = result.ShouldBeOk().ShouldBeOfType<VisaResource.Tcpip>();
        tcpip.Board.ShouldBe(0);
        tcpip.Host.ShouldBe("192.168.0.10");
        tcpip.LanDevice.ShouldBe("inst0");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a resource at all")]
    [InlineData("TCPIP0::192.168.0.10")] // missing suffix
    [InlineData("TCPIP0::192.168.0.10::inst0::NOTINSTR")] // wrong suffix
    [InlineData("TCPIPx::host::inst0::INSTR")] // non-numeric board
    [InlineData("TCPIP0::::inst0::INSTR")] // empty host
    [InlineData("TCPIP0::host::::INSTR")] // empty lan device
    [InlineData("TCPIP0::host::inst0::INSTR::EXTRA")] // too many segments
    public void Parse_InvalidInput_ReturnsInvalidVisaResourceFormat(string raw)
    {
        // Given / When
        var result = VisaResource.Parse(raw);

        // Then
        var err = result.ShouldBeError().ShouldBeOfType<InvalidVisaResourceFormat>();
        err.Raw.ShouldBe(raw);
    }

    [Fact]
    public void Tcpip_Equality_IsByValue()
    {
        // Given
        var a = VisaResource.Parse("TCPIP0::host::inst0::INSTR").ShouldBeOk();
        var b = VisaResource.Parse("TCPIP0::host::inst0::INSTR").ShouldBeOk();
        var c = VisaResource.Parse("TCPIP1::host::inst0::INSTR").ShouldBeOk();

        // When / Then
        a.ShouldBe(b);
        a.ShouldNotBe(c);
    }
}
