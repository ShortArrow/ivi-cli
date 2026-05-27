using System.Collections.Immutable;
using IviCli.Application.Capture;
using IviCli.Application.Mock;
using IviCli.Domain.Mock;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Application.Tests.Mock;

public sealed class ImportScenarioFromTrafficCommandHandlerTests
{
    private static readonly DateTimeOffset T = new(2026, 5, 27, 12, 0, 0, TimeSpan.Zero);

    private static TrafficEvent E(
        TrafficOp op,
        string device = "psu1",
        string? data = null,
        string? response = null
    ) => new(T, device, op, data, response, Ok: true, LatencyMs: 5, Error: null);

    [Fact]
    public async Task HandleAsync_happy_path_persists_scenario_with_supplied_name()
    {
        var reader = new FakeReader(
            E(TrafficOp.Open),
            E(TrafficOp.Query, data: "*IDN?", response: "ACME,PSU,1,1.0"),
            E(TrafficOp.Write, data: "OUTP ON"),
            E(TrafficOp.Close)
        );
        var store = new FakeScenarioStore();
        var handler = new ImportScenarioFromTrafficCommandHandler(
            reader,
            new DefaultTrafficScenarioConverter(),
            store
        );

        var result = await handler.HandleAsync(
            new ImportScenarioFromTrafficCommand(
                "run.ndjson",
                "psu1-smoke",
                DeviceFilter: null,
                Force: false
            ),
            default
        );

        var summary = result.ShouldBeOk();
        summary.Name.Value.ShouldBe("psu1-smoke");
        summary.Device.ShouldBe("psu1");
        summary.Scenes.ShouldBe(2);
        var loaded = (await store.LoadAsync(summary.Name, default)).ShouldBeOk();
        loaded.Scenes.Length.ShouldBe(2);
        loaded.IdnDefault.ShouldBe("ACME,PSU,1,1.0");
    }

    [Fact]
    public async Task HandleAsync_invalid_scenario_name_surfaces_specifically()
    {
        var handler = new ImportScenarioFromTrafficCommandHandler(
            new FakeReader(),
            new DefaultTrafficScenarioConverter(),
            new FakeScenarioStore()
        );

        var result = await handler.HandleAsync(
            new ImportScenarioFromTrafficCommand(
                "run.ndjson",
                "BAD NAME WITH SPACES!",
                null,
                false
            ),
            default
        );

        result.ShouldBeError().ShouldBeOfType<ImportTrafficInvalidName>();
    }

    [Fact]
    public async Task HandleAsync_reader_io_failure_collapses_to_ImportTrafficIoFailure()
    {
        var handler = new ImportScenarioFromTrafficCommandHandler(
            new ThrowingReader(new FileNotFoundException("nope", "run.ndjson")),
            new DefaultTrafficScenarioConverter(),
            new FakeScenarioStore()
        );

        var result = await handler.HandleAsync(
            new ImportScenarioFromTrafficCommand("run.ndjson", "x", null, false),
            default
        );

        result.ShouldBeError().ShouldBeOfType<ImportTrafficIoFailure>();
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
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct
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

        public IAsyncEnumerable<TrafficEvent> ReadAsync(string path, CancellationToken ct) =>
            Enumerate(_ex);

        private static async IAsyncEnumerable<TrafficEvent> Enumerate(Exception ex)
        {
            await Task.Yield();
            throw ex;
#pragma warning disable CS0162 // emit a yield to satisfy the async-iterator contract
            yield break;
#pragma warning restore CS0162
        }
    }
}
