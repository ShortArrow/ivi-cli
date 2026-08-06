using IviCli.Application.Backends;
using IviCli.Application.Devices;
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

    [Fact]
    public void RenderHuman_collapses_a_group_that_repeats_its_own_key()
    {
        // Given a lone USB resource, whose group key is the resource string itself
        var scan = ScanOf(Discovered("USB0::0x0b3e::0x1049::DN001677::INSTR"));

        // When the human listing is rendered
        var lines = VisaScanCommand.RenderHuman(scan);

        // Then the header carries the resource and no child repeats it
        lines.ShouldBe(["[1] USB0::0x0b3e::0x1049::DN001677::INSTR"]);
    }

    [Fact]
    public void RenderHuman_keeps_idn_and_detail_on_a_collapsed_group()
    {
        var scan = ScanOf(
            Discovered("USB0::0x0b3e::0x1049::DN001677::INSTR", "ACME,PSU,1,2.0", "USBTMC")
        );

        var lines = VisaScanCommand.RenderHuman(scan);

        lines.ShouldBe(["[1] USB0::0x0b3e::0x1049::DN001677::INSTR   [ACME,PSU,1,2.0]   (USBTMC)"]);
    }

    [Fact]
    public void RenderHuman_keeps_the_header_and_children_for_a_multi_path_host()
    {
        // Given one host reachable over two access paths
        var scan = ScanOf(
            Discovered("TCPIP0::192.168.3.10::inst0::INSTR"),
            Discovered("TCPIP0::192.168.3.10::5025::SOCKET")
        );

        // When the human listing is rendered
        var lines = VisaScanCommand.RenderHuman(scan);

        // Then the host heads the group and both paths stay indented under it
        lines.ShouldBe([
            "[1] 192.168.3.10",
            "      TCPIP0::192.168.3.10::5025::SOCKET",
            "      TCPIP0::192.168.3.10::inst0::INSTR",
        ]);
    }

    [Fact]
    public void RenderHuman_keeps_the_header_for_a_lone_tcpip_resource()
    {
        var scan = ScanOf(Discovered("TCPIP0::192.168.3.10::inst0::INSTR"));

        var lines = VisaScanCommand.RenderHuman(scan);

        lines.ShouldBe(["[1] 192.168.3.10", "      TCPIP0::192.168.3.10::inst0::INSTR"]);
    }

    [Fact]
    public void RenderHuman_reports_an_empty_scan()
    {
        VisaScanCommand.RenderHuman(new ScanResult([])).ShouldBe(["(no resources discovered)"]);
    }

    private static ScanResult ScanOf(params DiscoveredResource[] resources) => new([.. resources]);

    private static DiscoveredResource Discovered(
        string raw,
        string? idn = null,
        string? detail = null
    ) => new(VisaResource.Parse(raw).ShouldBeOk(), idn, detail);

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
