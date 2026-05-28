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

/// <summary>
/// Verifies that when the Management API host is configured with a
/// pool layer, repeated query calls share one inner backend open/close
/// (ADR 0038).
/// </summary>
public sealed class PooledApiTests
{
    [Fact]
    public async Task Two_consecutive_queries_through_API_share_one_inner_open()
    {
        var deviceName = DeviceName.From("psu1").ShouldBeOk();
        var device = new Device(
            deviceName,
            VisaResource.Parse("TCPIP0::192.168.0.10::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );
        var doc = ConfigDocument.Empty.AddDevice(device).ShouldBeOk();
        var backend = new FakeBackend().RespondToQuery(deviceName, "*IDN?", "ACME,PSU,1,1.0");

        await using var host = await ApiTestHost.StartAsync(
            doc,
            backend,
            poolConfig: PoolConfig.Default
        );

        for (var i = 0; i < 2; i++)
        {
            var resp = await host.Client.PostAsJsonAsync(
                "/v1/devices/psu1/query",
                new ScpiRequestDto("*IDN?")
            );
            resp.IsSuccessStatusCode.ShouldBeTrue();
        }

        backend.OpenCountFor(deviceName).ShouldBe(1);
        backend.CloseCountFor(deviceName).ShouldBe(0);
    }
}
