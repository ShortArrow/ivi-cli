using System.Collections.Immutable;
using IviCli.Application.Devices;
using IviCli.Application.Watch;
using IviCli.Cli.Watch;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.TestKit;

namespace IviCli.Cli.Tests.Watch;

public sealed class PlainTableSinkTests
{
    private static DeviceStatus Snap(string name, bool online, int latencyMs, string? idn)
    {
        var device = new Device(
            DeviceName.From(name).ShouldBeOk(),
            VisaResource.Parse("TCPIP0::host::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );
        return new DeviceStatus(
            device,
            online,
            TimeSpan.FromMilliseconds(latencyMs),
            online ? idn : null,
            online ? null : idn
        );
    }

    [Fact]
    public async Task EmitAsync_writes_tick_header_and_one_row_per_snapshot()
    {
        var writer = new StringWriter();
        var sink = new PlainTableSink(writer);
        var tick = new WatchTick(
            new DateTimeOffset(2026, 5, 27, 12, 0, 0, TimeSpan.Zero),
            Sequence: 7,
            Snapshots: ImmutableArray.Create(
                Snap("psu1", true, 12, "ACME,PSU,001,1.0"),
                Snap("dmm1", false, 5000, "connect failed")
            )
        );

        await sink.EmitAsync(tick, default);

        var output = writer.ToString();
        output.ShouldContain("# tick 7");
        output.ShouldContain("2026-05-27T12:00:00.0000000+00:00");
        output.ShouldContain("psu1");
        output.ShouldContain("yes");
        output.ShouldContain("ACME,PSU,001,1.0");
        output.ShouldContain("dmm1");
        output.ShouldContain("no");
        output.ShouldContain("connect failed");
    }

    [Fact]
    public async Task EmitAsync_handles_empty_snapshots_with_header_only()
    {
        var writer = new StringWriter();
        var sink = new PlainTableSink(writer);
        var tick = new WatchTick(
            DateTimeOffset.UnixEpoch,
            Sequence: 0,
            Snapshots: ImmutableArray<DeviceStatus>.Empty
        );

        await sink.EmitAsync(tick, default);

        writer.ToString().ShouldContain("# tick 0");
    }
}
