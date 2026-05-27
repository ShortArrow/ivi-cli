using System.Collections.Immutable;
using System.Text.Json;
using IviCli.Application.Devices;
using IviCli.Application.Watch;
using IviCli.Cli.Watch;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.TestKit;

namespace IviCli.Cli.Tests.Watch;

public sealed class NdjsonSinkTests
{
    [Fact]
    public async Task EmitAsync_writes_a_single_parseable_json_line_per_tick()
    {
        var writer = new StringWriter();
        var sink = new NdjsonSink(writer);
        var device = new Device(
            DeviceName.From("psu1").ShouldBeOk(),
            VisaResource.Parse("TCPIP0::host::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );
        var status = new DeviceStatus(
            device,
            IsOnline: true,
            ResponseTime: TimeSpan.FromMilliseconds(42),
            IdnResponse: "ACME,PSU,001,1.0",
            FailureMessage: null
        );
        var tick = new WatchTick(
            DateTimeOffset.UnixEpoch.AddSeconds(1),
            Sequence: 3,
            Snapshots: ImmutableArray.Create(status)
        );

        await sink.EmitAsync(tick, default);

        var line = writer.ToString().TrimEnd('\r', '\n');
        line.ShouldNotContain("\n");
        using var doc = JsonDocument.Parse(line);
        doc.RootElement.GetProperty("sequence").GetInt32().ShouldBe(3);
        var snapshots = doc.RootElement.GetProperty("snapshots");
        snapshots.GetArrayLength().ShouldBe(1);
        var first = snapshots[0];
        first.GetProperty("device").GetString().ShouldBe("psu1");
        first.GetProperty("online").GetBoolean().ShouldBeTrue();
        first.GetProperty("latencyMs").GetInt32().ShouldBe(42);
        first.GetProperty("idn").GetString().ShouldBe("ACME,PSU,001,1.0");
    }
}
