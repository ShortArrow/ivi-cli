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
    public void ToCanonical_round_trips_through_parse(string raw)
    {
        var resource = VisaResource.Parse(raw).ShouldBeOk();

        resource.ToCanonical().ShouldBe(raw);
    }

    [Fact]
    public void ToCanonical_is_unmasked_unlike_ToLogString()
    {
        var resource = VisaResource.Parse("TCPIP0::192.168.0.10::inst0::INSTR").ShouldBeOk();

        resource.ToCanonical().ShouldBe("TCPIP0::192.168.0.10::inst0::INSTR");
        resource.ToLogString().ShouldContain("***");
    }
}
