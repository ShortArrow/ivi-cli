using System.IO.Abstractions.TestingHelpers;
using IviCli.Application.Capture;
using IviCli.Application.Mock;
using IviCli.Infrastructure.Capture;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Infrastructure.Tests.Capture;

/// <summary>
/// End-to-end for requirement 2 across the real NDJSON adapters: writes a
/// serving gateway captured are read back out-of-process and confirmed by
/// device and SCPI. Exercises the actual on-disk format (not a fake reader),
/// so an enum-casing or schema drift between writer and query surfaces here.
/// </summary>
public sealed class MockWritesCaptureRoundTripTests
{
    private const string Path = "/var/log/ivi-cli/run.ndjson";

    private static TrafficEvent Write(string device, string scpi) =>
        new(
            new DateTimeOffset(2026, 5, 27, 12, 0, 0, TimeSpan.Zero),
            device,
            TrafficOp.Write,
            scpi,
            Response: null,
            Ok: true,
            LatencyMs: null,
            Error: null
        );

    private static TrafficEvent Query(string device, string scpi) =>
        new(
            new DateTimeOffset(2026, 5, 27, 12, 0, 1, TimeSpan.Zero),
            device,
            TrafficOp.Query,
            scpi,
            Response: "1.234",
            Ok: true,
            LatencyMs: 2,
            Error: null
        );

    [Fact]
    public async Task Last_VOLT_and_CURR_writes_are_confirmable_out_of_process()
    {
        var fs = new MockFileSystem();
        var writer = new NdjsonTrafficWriter(fs, Path);
        // Simulate what a serving gateway captures as a client drives it.
        await writer.AppendAsync(Write("psu1", ":VOLT 12.000"), default);
        await writer.AppendAsync(Query("psu1", ":MEAS:VOLT?"), default);
        await writer.AppendAsync(Write("psu1", ":VOLT 24.000"), default);
        await writer.AppendAsync(Write("psu1", ":CURR 3.300"), default);
        await writer.AppendAsync(Write("other", ":VOLT 99.000"), default);

        var handler = new MockWritesQueryHandler(new NdjsonTrafficReader(fs));

        var volt = (
            await handler.HandleAsync(new MockWritesQuery("psu1", ":VOLT", Path), default)
        ).ShouldBeOk();
        volt.Last().Data.ShouldBe(":VOLT 24.000");

        var curr = (
            await handler.HandleAsync(new MockWritesQuery("psu1", ":CURR", Path), default)
        ).ShouldBeOk();
        curr.Last().Data.ShouldBe(":CURR 3.300");
    }

    [Fact]
    public async Task Writes_for_other_devices_are_not_reported()
    {
        var fs = new MockFileSystem();
        var writer = new NdjsonTrafficWriter(fs, Path);
        await writer.AppendAsync(Write("other", ":VOLT 99.000"), default);

        var handler = new MockWritesQueryHandler(new NdjsonTrafficReader(fs));

        var volt = (
            await handler.HandleAsync(new MockWritesQuery("psu1", ":VOLT", Path), default)
        ).ShouldBeOk();
        volt.ShouldBeEmpty();
    }
}
