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
        var resp = await host.Client.GetAsync("/healthz", TestContext.Current.CancellationToken);
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GET_devices_returns_listing_with_default()
    {
        var doc = ConfigDocument.Empty.AddDevice(Dev("psu1")).ShouldBeOk();
        doc = doc.AddDevice(Dev("dmm1")).ShouldBeOk();
        doc = doc.SetDefaultDevice(DeviceName.From("psu1").ShouldBeOk()).ShouldBeOk();
        await using var host = await ApiTestHost.StartAsync(doc);

        var listing = await host.Client.GetFromJsonAsync<DeviceListingDto>(
            "/v1/devices",
            cancellationToken: TestContext.Current.CancellationToken
        );
        listing.ShouldNotBeNull();
        listing!.Devices.Count.ShouldBe(2);
        listing.Default.ShouldBe("psu1");
        listing.Devices[0].Name.ShouldBe("psu1");
    }

    [Fact]
    public async Task GET_devices_returns_503_with_the_store_error_rendered()
    {
        var store = new FakeConfigStore(ConfigDocument.Empty);
        store.FailNextLoadWith("disk gone");
        await using var host = await ApiTestHost.StartAsync(
            ConfigDocument.Empty,
            configStore: store
        );

        var resp = await host.Client.GetAsync("/v1/devices", TestContext.Current.CancellationToken);

        resp.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var error = await resp.Content.ReadFromJsonAsync<ErrorDto>(
            cancellationToken: TestContext.Current.CancellationToken
        );
        error!.Error.Code.ShouldBe("config_store_failure");
        error.Error.Message.ShouldBe("the configuration or session store could not be used");
    }

    [Fact]
    public async Task GET_devices_shows_the_serial_line_and_the_terminators_that_are_set()
    {
        var serial = new Device(
            DeviceName.From("dmm").ShouldBeOk(),
            VisaResource.Parse("ASRL1::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(2000).ShouldBeOk(),
            SerialSettings
                .From(
                    19200,
                    7,
                    SerialParity.Even,
                    SerialStopBits.OnePointFive,
                    SerialFlowControl.RtsCts
                )
                .ShouldBeOk(),
            ReadTermination: Termination.Cr,
            WriteTermination: Termination.CrLf
        );
        var doc = ConfigDocument
            .Empty.AddDevice(serial)
            .ShouldBeOk()
            .AddDevice(Dev("psu1"))
            .ShouldBeOk();
        await using var host = await ApiTestHost.StartAsync(doc);

        var body = await host.Client.GetStringAsync(
            "/v1/devices",
            TestContext.Current.CancellationToken
        );

        using var json = System.Text.Json.JsonDocument.Parse(body);
        var devices = json.RootElement.GetProperty("devices");
        devices[0]
            .GetProperty("serial")
            .GetRawText()
            .ShouldBe(
                """{"baud":19200,"dataBits":7,"parity":"even","stopBits":1.5,"flowControl":"rts_cts"}"""
            );
        devices[0].GetProperty("readTermination").GetString().ShouldBe("\r");
        devices[0].GetProperty("writeTermination").GetString().ShouldBe("\r\n");
        devices[1].TryGetProperty("serial", out _).ShouldBeFalse();
        devices[1].TryGetProperty("readTermination", out _).ShouldBeFalse();
        devices[1].TryGetProperty("writeTermination", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task GET_device_status_returns_online_when_backend_responds()
    {
        var doc = ConfigDocument.Empty.AddDevice(Dev("psu1")).ShouldBeOk();
        var backend = new FakeBackend();
        backend.RespondToQuery(DeviceName.From("psu1").ShouldBeOk(), "*IDN?", "ACME,PSU,1,1.0");
        await using var host = await ApiTestHost.StartAsync(doc, backend);

        var status = await host.Client.GetFromJsonAsync<DeviceStatusDto>(
            "/v1/devices/psu1/status",
            cancellationToken: TestContext.Current.CancellationToken
        );
        status.ShouldNotBeNull();
        status!.Online.ShouldBeTrue();
        status.Idn.ShouldBe("ACME,PSU,1,1.0");
    }

    [Fact]
    public async Task GET_device_status_returns_404_for_unknown_alias()
    {
        await using var host = await ApiTestHost.StartAsync(ConfigDocument.Empty);

        var resp = await host.Client.GetAsync(
            "/v1/devices/nope/status",
            TestContext.Current.CancellationToken
        );
        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var error = await resp.Content.ReadFromJsonAsync<ErrorDto>(
            cancellationToken: TestContext.Current.CancellationToken
        );
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

        var listing = await host.Client.GetFromJsonAsync<ServerListingDto>(
            "/v1/servers",
            cancellationToken: TestContext.Current.CancellationToken
        );
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

        var listing = await host.Client.GetFromJsonAsync<ScenarioListingDto>(
            "/v1/scenarios",
            cancellationToken: TestContext.Current.CancellationToken
        );
        listing.ShouldNotBeNull();
        listing!.Scenarios.Count.ShouldBe(2);
        listing.Scenarios.ShouldContain("psu1-smoke");
        listing.Scenarios.ShouldContain("dmm1-warmup");
    }
}
