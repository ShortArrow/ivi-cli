using IviCli.Application.Devices;
using IviCli.Cli.Commands;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Cli.Tests.Commands;

/// <summary>
/// Locks the <c>visa status --json</c> stdout contract: its field names, the
/// masked resource, and the number form of the response time.
/// </summary>
public sealed class DeviceStatusFormatterTests
{
    private static Device Dmm() =>
        new(
            DeviceName.From("dmm").ShouldBeOk(),
            VisaResource.Parse("TCPIP0::192.0.2.1::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk(),
            null,
            null,
            null
        );

    [Fact]
    public void FormatJson_writes_an_online_device_with_its_idn_and_no_failure()
    {
        var status = new DeviceStatus(
            Dmm(),
            IsOnline: true,
            ResponseTime: TimeSpan.FromMilliseconds(12.34),
            IdnResponse: "ACME,DMM,1,1.0",
            FailureMessage: null
        );

        DeviceStatusFormatter
            .FormatJson(status)
            .ShouldBe(
                """{"device":"dmm","resource":"TCPIP0::***::inst0::INSTR","online":true,"response_time_ms":12.3,"idn":"ACME,DMM,1,1.0","failure":null}"""
            );
    }

    [Fact]
    public void FormatJson_writes_an_offline_device_with_its_failure_and_no_idn()
    {
        var status = new DeviceStatus(
            Dmm(),
            IsOnline: false,
            ResponseTime: TimeSpan.FromMilliseconds(3000),
            IdnResponse: null,
            FailureMessage: "timed out"
        );

        DeviceStatusFormatter
            .FormatJson(status)
            .ShouldBe(
                """{"device":"dmm","resource":"TCPIP0::***::inst0::INSTR","online":false,"response_time_ms":3000.0,"idn":null,"failure":"timed out"}"""
            );
    }
}
