using IviCli.Domain.Devices;
using IviCli.Domain.Servers;
using IviCli.TestKit;

namespace IviCli.Domain.Tests.Servers;

/// <summary>
/// A route names which USB profile its exported device presents
/// (ADR 0049 §5). The property is additive: every route written before
/// the profile existed means the same thing it meant then.
/// </summary>
public sealed class RouteTests
{
    [Fact]
    public void A_route_exports_the_instrument_profile_unless_told_otherwise()
    {
        Build().Profile.ShouldBe(UsbExportProfile.UsbTmc);
    }

    [Fact]
    public void A_route_carrying_the_serial_profile_keeps_it()
    {
        var route = Build() with { Profile = UsbExportProfile.CdcAcm };

        route.Profile.ShouldBe(UsbExportProfile.CdcAcm);
    }

    [Fact]
    public void Two_routes_that_differ_only_in_profile_are_different_routes()
    {
        var usbtmc = Build();
        var cdc = usbtmc with { Profile = UsbExportProfile.CdcAcm };

        cdc.ShouldNotBe(usbtmc);
    }

    private static Route Build() =>
        new(
            ServerName.From("usb-srv").ShouldBeOk(),
            PublicEndpoint.From("1-1").ShouldBeOk(),
            DeviceName.From("dut").ShouldBeOk()
        );
}
