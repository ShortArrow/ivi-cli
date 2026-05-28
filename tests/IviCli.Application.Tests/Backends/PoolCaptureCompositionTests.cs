using IviCli.Application.Backends;
using IviCli.Application.Capture;
using IviCli.Backends.Fake;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace IviCli.Application.Tests.Backends;

/// <summary>
/// Verifies the production composition order
/// <c>CapturingBackendFactory(PoolingBackendFactory(DefaultFactory))</c>
/// behaves per ADR 0038 §5: capture observes every logical Open/Close
/// even when the pool elides the underlying wire opens.
/// </summary>
public sealed class PoolCaptureCompositionTests
{
    private static Device Dev() =>
        new(
            DeviceName.From("dut").ShouldBeOk(),
            VisaResource.Parse("TCPIP0::127.0.0.1::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(1000).ShouldBeOk()
        );

    [Fact]
    public async Task Pool_inside_Capture_elides_inner_open_but_records_caller_events()
    {
        var fake = new FakeBackend().RespondToQuery(Dev().Name, "*IDN?", "FAKE,FAKE,0,1.0");
        var writer = new RecordingWriter();
        var time = new FakeTimeProvider();

        await using var pool = new PoolingBackendFactory(
            new FakeBackendFactory(fake),
            PoolConfig.Default,
            time
        );
        var capture = new CapturingBackendFactory(pool, writer);

        for (var i = 0; i < 2; i++)
        {
            var backend = capture.CreateFor(Dev()).ShouldBeOk();
            (await backend.OpenAsync(Dev(), default)).ShouldBeOk();
            (
                await backend.QueryAsync(Dev(), ScpiQuery.From("*IDN?").ShouldBeOk(), default)
            ).ShouldBeOk();
            (await backend.CloseAsync(Dev(), default)).ShouldBeOk();
        }

        // Pool elision: one real inner open across two logical cycles.
        fake.OpenCountFor(Dev().Name).ShouldBe(1);
        fake.CloseCountFor(Dev().Name).ShouldBe(0);

        // Capture sees every logical caller event (2 opens, 2 queries, 2 closes).
        writer.Events.Count(e => e.Op == TrafficOp.Open).ShouldBe(2);
        writer.Events.Count(e => e.Op == TrafficOp.Query).ShouldBe(2);
        writer.Events.Count(e => e.Op == TrafficOp.Close).ShouldBe(2);
    }

    private sealed class RecordingWriter : ITrafficWriter
    {
        public List<TrafficEvent> Events { get; } = new();

        public Task AppendAsync(TrafficEvent ev, CancellationToken ct)
        {
            Events.Add(ev);
            return Task.CompletedTask;
        }
    }
}
