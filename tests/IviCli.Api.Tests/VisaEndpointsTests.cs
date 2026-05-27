using System.Net;
using System.Net.Http.Json;
using IviCli.Api.Contracts;
using IviCli.Backends.Fake;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Api.Tests;

public sealed class VisaEndpointsTests
{
    private static Device Dev(string name) =>
        new(
            DeviceName.From(name).ShouldBeOk(),
            VisaResource.Parse("TCPIP0::192.168.0.10::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    [Fact]
    public async Task POST_query_round_trips_through_backend()
    {
        var doc = ConfigDocument.Empty.AddDevice(Dev("psu1")).ShouldBeOk();
        var backend = new FakeBackend();
        backend.RespondToQuery(DeviceName.From("psu1").ShouldBeOk(), "*IDN?", "ACME,PSU,1,1.0");
        await using var host = await ApiTestHost.StartAsync(doc, backend);

        var resp = await host.Client.PostAsJsonAsync(
            "/v1/devices/psu1/query",
            new ScpiRequestDto("*IDN?")
        );

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await resp.Content.ReadFromJsonAsync<ScpiQueryResponseDto>();
        dto!.Response.ShouldBe("ACME,PSU,1,1.0");
    }

    [Fact]
    public async Task POST_write_returns_ack_on_success()
    {
        var doc = ConfigDocument.Empty.AddDevice(Dev("psu1")).ShouldBeOk();
        await using var host = await ApiTestHost.StartAsync(doc);

        var resp = await host.Client.PostAsJsonAsync(
            "/v1/devices/psu1/write",
            new ScpiRequestDto("OUTP ON")
        );

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var ack = await resp.Content.ReadFromJsonAsync<ScpiAckDto>();
        ack!.Ok.ShouldBeTrue();
    }

    [Fact]
    public async Task POST_query_returns_400_on_missing_scpi_field()
    {
        var doc = ConfigDocument.Empty.AddDevice(Dev("psu1")).ShouldBeOk();
        await using var host = await ApiTestHost.StartAsync(doc);

        var resp = await host.Client.PostAsJsonAsync(
            "/v1/devices/psu1/query",
            new ScpiRequestDto("")
        );

        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var error = await resp.Content.ReadFromJsonAsync<ErrorDto>();
        error!.Error.Code.ShouldBe("missing_scpi");
    }

    [Fact]
    public async Task POST_query_returns_400_on_invalid_scpi()
    {
        var doc = ConfigDocument.Empty.AddDevice(Dev("psu1")).ShouldBeOk();
        await using var host = await ApiTestHost.StartAsync(doc);

        // Missing '?' — QueryDevice handler rejects this.
        var resp = await host.Client.PostAsJsonAsync(
            "/v1/devices/psu1/query",
            new ScpiRequestDto("*IDN")
        );

        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var error = await resp.Content.ReadFromJsonAsync<ErrorDto>();
        error!.Error.Code.ShouldBe("invalid_scpi");
    }

    [Fact]
    public async Task POST_query_returns_404_for_unknown_device()
    {
        await using var host = await ApiTestHost.StartAsync(ConfigDocument.Empty);

        var resp = await host.Client.PostAsJsonAsync(
            "/v1/devices/nope/query",
            new ScpiRequestDto("*IDN?")
        );

        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var error = await resp.Content.ReadFromJsonAsync<ErrorDto>();
        error!.Error.Code.ShouldBe("device_not_found");
    }
}
