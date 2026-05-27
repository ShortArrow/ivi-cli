using System.Net;
using System.Net.Http.Json;
using IviCli.Api.Contracts;
using IviCli.Backends.Fake;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Api.Tests;

public sealed class ReadOnlyEndpointsTests
{
    private static Device Dev(string name) =>
        new(
            DeviceName.From(name).ShouldBeOk(),
            VisaResource.Parse("TCPIP0::192.168.0.10::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    private static MockScenario Scn(string name) =>
        MockScenario.Empty(ScenarioName.From(name).ShouldBeOk());

    [Fact]
    public async Task GET_healthz_returns_200_ok()
    {
        await using var host = await ApiTestHost.StartAsync(ConfigDocument.Empty);
        var resp = await host.Client.GetAsync("/healthz");
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GET_devices_returns_listing_with_default()
    {
        var doc = ConfigDocument.Empty.AddDevice(Dev("psu1")).ShouldBeOk();
        doc = doc.AddDevice(Dev("dmm1")).ShouldBeOk();
        doc = doc.SetDefaultDevice(DeviceName.From("psu1").ShouldBeOk()).ShouldBeOk();
        await using var host = await ApiTestHost.StartAsync(doc);

        var listing = await host.Client.GetFromJsonAsync<DeviceListingDto>("/v1/devices");
        listing.ShouldNotBeNull();
        listing!.Devices.Count.ShouldBe(2);
        listing.Default.ShouldBe("psu1");
        listing.Devices[0].Name.ShouldBe("psu1");
    }

    [Fact]
    public async Task GET_device_status_returns_online_when_backend_responds()
    {
        var doc = ConfigDocument.Empty.AddDevice(Dev("psu1")).ShouldBeOk();
        var backend = new FakeBackend();
        backend.RespondToQuery(DeviceName.From("psu1").ShouldBeOk(), "*IDN?", "ACME,PSU,1,1.0");
        await using var host = await ApiTestHost.StartAsync(doc, backend);

        var status = await host.Client.GetFromJsonAsync<DeviceStatusDto>("/v1/devices/psu1/status");
        status.ShouldNotBeNull();
        status!.Online.ShouldBeTrue();
        status.Idn.ShouldBe("ACME,PSU,1,1.0");
    }

    [Fact]
    public async Task GET_device_status_returns_404_for_unknown_alias()
    {
        await using var host = await ApiTestHost.StartAsync(ConfigDocument.Empty);

        var resp = await host.Client.GetAsync("/v1/devices/nope/status");
        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var error = await resp.Content.ReadFromJsonAsync<ErrorDto>();
        error!.Error.Code.ShouldBe("device_not_found");
    }

    [Fact]
    public async Task GET_servers_returns_listing()
    {
        var doc = ConfigDocument
            .Empty.AddServer(
                new IviCli.Domain.Servers.Server(
                    ServerName.From("hislip-srv").ShouldBeOk(),
                    ServerType.HiSlip,
                    IpAddress.From("127.0.0.1").ShouldBeOk(),
                    Port.From(4880).ShouldBeOk()
                )
            )
            .ShouldBeOk();
        await using var host = await ApiTestHost.StartAsync(doc);

        var listing = await host.Client.GetFromJsonAsync<ServerListingDto>("/v1/servers");
        listing.ShouldNotBeNull();
        listing!.Servers.Count.ShouldBe(1);
        listing.Servers[0].Name.ShouldBe("hislip-srv");
        listing.Servers[0].Type.ShouldBe("HiSlip");
        listing.Servers[0].Port.ShouldBe(4880);
    }

    [Fact]
    public async Task GET_scenarios_returns_listing()
    {
        await using var host = await ApiTestHost.StartAsync(
            ConfigDocument.Empty,
            scenarios: new[] { Scn("psu1-smoke"), Scn("dmm1-warmup") }
        );

        var listing = await host.Client.GetFromJsonAsync<ScenarioListingDto>("/v1/scenarios");
        listing.ShouldNotBeNull();
        listing!.Scenarios.Count.ShouldBe(2);
        listing.Scenarios.ShouldContain("psu1-smoke");
        listing.Scenarios.ShouldContain("dmm1-warmup");
    }
}
