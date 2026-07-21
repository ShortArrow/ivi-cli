using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using IviCli.Application.Capture;
using IviCli.Application.Mock;
using IviCli.Domain;
using IviCli.TestKit;
using Shouldly;
using Xunit;

namespace IviCli.Application.Tests.Mock;

/// <summary>
/// Locks in the out-of-process write-observation contract: reading the NDJSON
/// capture a serving gateway produced, a separate process can confirm which
/// SCPI writes a device actually received.
/// </summary>
public sealed class MockReceivedWritesQueryHandlerTests
{
    private static TrafficEvent Write(string device, string scpi, bool ok = true) =>
        new(
            default,
            device,
            TrafficOp.Write,
            scpi,
            Response: null,
            ok,
            LatencyMs: null,
            Error: null
        );

    private static TrafficEvent Query(string device, string scpi) =>
        new(
            default,
            device,
            TrafficOp.Query,
            scpi,
            Response: "x",
            Ok: true,
            LatencyMs: 1,
            Error: null
        );

    [Fact]
    public async Task Returns_matching_writes_for_the_device_in_order()
    {
        var handler = new MockReceivedWritesQueryHandler(
            new FakeReader(
                Write("dut", ":VOLT 1.000"),
                Query("dut", ":MEAS:VOLT?"),
                Write("dut", ":VOLT 24.000"),
                Write("other", ":VOLT 9.000")
            )
        );

        var result = await handler.HandleAsync(
            new MockReceivedWritesQuery("dut", ":VOLT", null, "run.ndjson"),
            default
        );

        var writes = result.ShouldBeOk();
        var expected = new[] { ":VOLT 1.000", ":VOLT 24.000" };
        writes.Select(w => w.Data).ShouldBe(expected);
    }

    [Fact]
    public async Task Last_matching_write_is_the_most_recent()
    {
        var handler = new MockReceivedWritesQueryHandler(
            new FakeReader(Write("dut", ":VOLT 1.000"), Write("dut", ":VOLT 24.000"))
        );

        var result = await handler.HandleAsync(
            new MockReceivedWritesQuery("dut", ":VOLT", null, "run.ndjson"),
            default
        );

        result.ShouldBeOk().Last().Data.ShouldBe(":VOLT 24.000");
    }

    [Fact]
    public async Task Filters_out_other_devices_and_non_write_ops()
    {
        var handler = new MockReceivedWritesQueryHandler(
            new FakeReader(Write("other", ":CURR 3.300"), Query("dut", ":CURR?"))
        );

        var result = await handler.HandleAsync(
            new MockReceivedWritesQuery("dut", ":CURR", null, "run.ndjson"),
            default
        );

        result.ShouldBeOk().ShouldBeEmpty();
    }

    [Fact]
    public async Task No_match_filter_returns_all_writes_for_the_device()
    {
        var handler = new MockReceivedWritesQueryHandler(
            new FakeReader(Write("dut", ":VOLT 1.000"), Write("dut", ":CURR 3.300"))
        );

        var result = await handler.HandleAsync(
            new MockReceivedWritesQuery("dut", Match: null, Exact: null, Path: "run.ndjson"),
            default
        );

        var expected = new[] { ":VOLT 1.000", ":CURR 3.300" };
        result.ShouldBeOk().Select(w => w.Data).ShouldBe(expected);
    }

    [Fact]
    public async Task Exact_filter_matches_only_the_full_scpi()
    {
        // Substring ':VOLT' would also catch ':VOLT:PROT 30'; --exact must not.
        var handler = new MockReceivedWritesQueryHandler(
            new FakeReader(Write("dut", ":VOLT 24.000"), Write("dut", ":VOLT:PROT 30"))
        );

        var result = await handler.HandleAsync(
            new MockReceivedWritesQuery("dut", Match: null, Exact: ":VOLT 24.000", "run.ndjson"),
            default
        );

        result.ShouldBeOk().ShouldHaveSingleItem().Data.ShouldBe(":VOLT 24.000");
    }

    [Fact]
    public async Task Invalid_device_alias_is_rejected()
    {
        var handler = new MockReceivedWritesQueryHandler(new FakeReader());

        var result = await handler.HandleAsync(
            new MockReceivedWritesQuery("bad alias!", null, null, "run.ndjson"),
            default
        );

        result.ShouldBeError().ShouldBeOfType<MockReceivedWritesInvalidDevice>();
    }

    [Fact]
    public async Task Unreadable_capture_surfaces_an_io_failure()
    {
        var handler = new MockReceivedWritesQueryHandler(
            new ThrowingReader(new FileNotFoundException("missing"))
        );

        var result = await handler.HandleAsync(
            new MockReceivedWritesQuery("dut", null, null, "missing.ndjson"),
            default
        );

        result.ShouldBeError().ShouldBeOfType<MockReceivedWritesIoFailure>();
    }

    private sealed class FakeReader : INdjsonTrafficReader
    {
        private readonly ImmutableArray<TrafficEvent> _events;

        public FakeReader(params TrafficEvent[] events)
        {
            _events = events.ToImmutableArray();
        }

        public async IAsyncEnumerable<TrafficEvent> ReadAsync(
            string path,
            [EnumeratorCancellation] CancellationToken ct
        )
        {
            foreach (var ev in _events)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return ev;
            }
        }
    }

    private sealed class ThrowingReader : INdjsonTrafficReader
    {
        private readonly Exception _ex;

        public ThrowingReader(Exception ex)
        {
            _ex = ex;
        }

        public async IAsyncEnumerable<TrafficEvent> ReadAsync(
            string path,
            [EnumeratorCancellation] CancellationToken ct
        )
        {
            await Task.Yield();
            throw _ex;
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }
}
