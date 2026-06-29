using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Domain.Tests.Visa;

/// <summary>
/// <see cref="VisaResource.ToCanonical"/> renders the full, unmasked
/// resource string — the inverse of <see cref="VisaResource.Parse"/> and,
/// unlike <see cref="VisaResource.ToLogString"/>, safe to show in
/// user-requested output (e.g. <c>visa list</c>). Every case round-trips
/// back through <c>Parse</c> unchanged.
/// </summary>
public sealed class VisaResourceCanonicalTests
{
    [Theory]
    [InlineData("TCPIP0::192.168.0.10::inst0::INSTR")]
    [InlineData("TCPIP0::192.168.3.100::hislip0::INSTR")]
    [InlineData("TCPIP0::192.168.3.100::5025::SOCKET")]
    [InlineData("USB0::0x0699::0x0408::C012345::INSTR")]
    [InlineData("USB0::0x0699::0x0408::C012345::1::INSTR")]
    [InlineData("GPIB0::15::INSTR")]
    [InlineData("GPIB0::15::3::INSTR")]
    // Comma-port form (HiSLIP / VXI-11 non-standard port, VISA convention).
    [InlineData("TCPIP0::192.168.3.100::hislip0,5000::INSTR")]
    [InlineData("TCPIP0::192.168.3.100::inst0,20001::INSTR")]
    public void ToCanonical_round_trips_through_parse(string raw)
    {
        var resource = VisaResource.Parse(raw).ShouldBeOk();

        resource.ToCanonical().ShouldBe(raw);
    }

    [Theory]
    [InlineData("TCPIP0::host::hislip0,5000::INSTR", "hislip0", 5000)]
    [InlineData("TCPIP0::host::inst0,20001::INSTR", "inst0", 20001)]
    public void Parse_extracts_lan_device_and_explicit_port(
        string raw,
        string expectedLanDevice,
        int expectedPort
    )
    {
        var resource = VisaResource.Parse(raw).ShouldBeOk();

        var tcpip = resource.ShouldBeOfType<VisaResource.Tcpip>();
        tcpip.LanDevice.ShouldBe(expectedLanDevice);
        tcpip.Port.ShouldBe(expectedPort);
    }

    [Fact]
    public void Parse_leaves_port_null_when_no_comma()
    {
        var tcpip = VisaResource
            .Parse("TCPIP0::host::hislip0::INSTR")
            .ShouldBeOk()
            .ShouldBeOfType<VisaResource.Tcpip>();

        tcpip.Port.ShouldBeNull();
    }

    [Theory]
    [InlineData("TCPIP0::host::hislip0,::INSTR")] // empty port
    [InlineData("TCPIP0::host::hislip0,abc::INSTR")] // non-numeric port
    [InlineData("TCPIP0::host::hislip0,70000::INSTR")] // out of range
    [InlineData("TCPIP0::host::,5000::INSTR")] // empty lan device
    public void Parse_rejects_malformed_comma_port(string raw)
    {
        VisaResource.Parse(raw).ShouldBeError();
    }

    [Fact]
    public void ToCanonical_is_unmasked_unlike_ToLogString()
    {
        var resource = VisaResource.Parse("TCPIP0::192.168.0.10::inst0::INSTR").ShouldBeOk();

        resource.ToCanonical().ShouldBe("TCPIP0::192.168.0.10::inst0::INSTR");
        resource.ToLogString().ShouldContain("***");
    }
}
