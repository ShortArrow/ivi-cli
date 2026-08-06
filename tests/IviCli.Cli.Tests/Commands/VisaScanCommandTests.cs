using IviCli.Cli.Commands;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Cli.Tests.Commands;

/// <summary>
/// Locks in the pure helpers <see cref="VisaScanCommand.DeriveAlias"/>
/// and <see cref="VisaScanCommand.FormatResource"/> used by the
/// <c>visa scan --add</c> flow (Batch W).
/// </summary>
public sealed class VisaScanCommandTests
{
    [Theory]
    [InlineData("TCPIP0::192.168.1.10::hislip0::INSTR", "host_192_168_1_10")]
    [InlineData("TCPIP0::psu-lab.local::inst0::INSTR", "psu_lab_local")]
    public void DeriveAlias_TCPIP_uses_sanitised_host(string raw, string expected)
    {
        var resource = VisaResource.Parse(raw).ShouldBeOk();
        VisaScanCommand.DeriveAlias(resource).ShouldBe(expected);
    }

    [Fact]
    public void DeriveAlias_USB_includes_serial()
    {
        var resource = VisaResource.Parse("USB0::0x1234::0x5678::ABC987::INSTR").ShouldBeOk();
        VisaScanCommand.DeriveAlias(resource).ShouldBe("usb_abc987");
    }

    [Fact]
    public void DeriveAlias_GPIB_includes_primary_address()
    {
        var resource = VisaResource.Parse("GPIB0::17::INSTR").ShouldBeOk();
        VisaScanCommand.DeriveAlias(resource).ShouldBe("gpib_17");
    }

    [Theory]
    [InlineData("TCPIP0::192.168.1.10::hislip0::INSTR")]
    [InlineData("TCPIP0::psu-lab.local::inst0::INSTR")]
    [InlineData("TCPIP0::10.0.0.5::5025::SOCKET")]
    [InlineData("USB0::0x0b3e::0x1049::DN001677::INSTR")]
    [InlineData("USB0::0x1234::0x5678::ABC-987::0::INSTR")]
    [InlineData("GPIB0::17::INSTR")]
    public void DeriveAlias_always_yields_a_registrable_device_name(string raw)
    {
        // Given a discovered resource of any supported shape
        var resource = VisaResource.Parse(raw).ShouldBeOk();

        // When an alias is derived for auto-registration
        var alias = VisaScanCommand.DeriveAlias(resource);

        // Then the alias must satisfy the DeviceName grammar, or
        // `visa scan --add` cannot register what it just discovered
        Domain.Devices.DeviceName.From(alias).ShouldBeOk();
    }

    [Theory]
    [InlineData("TCPIP0::host::hislip0::INSTR")]
    [InlineData("TCPIP1::10.0.0.5::inst0::INSTR")]
    [InlineData("USB0::0x1234::0x5678::SN123::INSTR")]
    [InlineData("GPIB0::17::INSTR")]
    [InlineData("GPIB0::17::5::INSTR")]
    public void FormatResource_round_trips_through_parse(string raw)
    {
        var parsed = VisaResource.Parse(raw).ShouldBeOk();
        var rendered = VisaScanCommand.FormatResource(parsed);
        rendered.ShouldBe(raw);
        // And the rendered value must itself parse back to the same VO.
        VisaResource.Parse(rendered).ShouldBeOk().ShouldBe(parsed);
    }
}
