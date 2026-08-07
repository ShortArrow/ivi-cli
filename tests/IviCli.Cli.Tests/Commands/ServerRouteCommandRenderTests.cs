using System.Collections.Immutable;
using IviCli.Application.Servers;
using IviCli.Cli.Commands;
using IviCli.Domain.Devices;
using IviCli.Domain.Servers;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Cli.Tests.Commands;

/// <summary>
/// What <c>server route list</c> prints. The profile is shown only when
/// it is a choice, the same stance the TOML serializer takes: a listing
/// of routes that all export the default reads exactly as it did before
/// the profile existed.
/// </summary>
public sealed class ServerRouteCommandRenderTests
{
    [Fact]
    public void An_empty_listing_says_so()
    {
        var writer = new StringWriter();

        ServerRouteCommand.Render(new RouteListing(ImmutableArray<Route>.Empty), writer);

        writer.ToString().ShouldBe("(no routes configured)" + Environment.NewLine);
    }

    [Fact]
    public void A_route_on_the_default_profile_renders_endpoint_server_and_device()
    {
        var writer = new StringWriter();

        ServerRouteCommand.Render(Listing(Route("hislip0", UsbExportProfile.UsbTmc)), writer);

        writer.ToString().ShouldBe("[hislip0] gw1 -> psu1" + Environment.NewLine);
    }

    [Fact]
    public void A_cdc_acm_route_names_its_profile()
    {
        var writer = new StringWriter();

        ServerRouteCommand.Render(Listing(Route("1-2", UsbExportProfile.CdcAcm)), writer);

        writer.ToString().ShouldBe("[1-2] gw1 -> psu1 (cdc-acm)" + Environment.NewLine);
    }

    [Fact]
    public void A_mixed_listing_marks_only_the_route_that_chose()
    {
        var writer = new StringWriter();
        var listing = new RouteListing([
            Route("1-1", UsbExportProfile.UsbTmc),
            Route("1-2", UsbExportProfile.CdcAcm),
        ]);

        ServerRouteCommand.Render(listing, writer);

        writer
            .ToString()
            .ShouldBe(
                "[1-1] gw1 -> psu1"
                    + Environment.NewLine
                    + "[1-2] gw1 -> psu1 (cdc-acm)"
                    + Environment.NewLine
            );
    }

    private static RouteListing Listing(Route route) => new([route]);

    private static Route Route(string endpoint, UsbExportProfile profile) =>
        new(
            ServerName.From("gw1").ShouldBeOk(),
            PublicEndpoint.From(endpoint).ShouldBeOk(),
            DeviceName.From("psu1").ShouldBeOk()
        )
        {
            Profile = profile,
        };
}
