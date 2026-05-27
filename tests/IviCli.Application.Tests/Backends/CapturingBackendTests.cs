using IviCli.Application.Backends;
using IviCli.Application.Capture;
using IviCli.Backends.Fake;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Application.Tests.Backends;

public sealed class CapturingBackendTests
{
    private static Device Dev(string name) =>
        new(
            DeviceName.From(name).ShouldBeOk(),
            VisaResource.Parse("TCPIP0::host::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    [Fact]
    public async Task OpenAsync_emits_one_Open_event_and_forwards_result()
    {
        var sink = new RecordingWriter();
        var backend = new CapturingBackend(new FakeBackend(), sink);
        var device = Dev("psu1");

        var result = await backend.OpenAsync(device, default);

        result.ShouldBeOk();
        sink.Events.Count.ShouldBe(1);
        sink.Events[0].Op.ShouldBe(TrafficOp.Open);
        sink.Events[0].Device.ShouldBe("psu1");
        sink.Events[0].Ok.ShouldBeTrue();
        sink.Events[0].Data.ShouldBeNull();
        sink.Events[0].Response.ShouldBeNull();
    }

    [Fact]
    public async Task WriteAsync_records_command_text_and_Ok_true()
    {
        var sink = new RecordingWriter();
        var inner = new FakeBackend();
        var backend = new CapturingBackend(inner, sink);
        var device = Dev("psu1");
        var command = ScpiCommand.From("OUTP ON").ShouldBeOk();

        await backend.OpenAsync(device, default);
        var result = await backend.WriteAsync(device, command, default);

        result.ShouldBeOk();
        var write = sink.Events.Last();
        write.Op.ShouldBe(TrafficOp.Write);
        write.Data.ShouldBe("OUTP ON");
        write.Ok.ShouldBeTrue();
    }

    [Fact]
    public async Task QueryAsync_records_response_and_non_null_latency()
    {
        var inner = new FakeBackend();
        inner.RespondToQuery(DeviceName.From("psu1").ShouldBeOk(), "*IDN?", "ACME,PSU,1,1.0");
        var sink = new RecordingWriter();
        var backend = new CapturingBackend(inner, sink);
        var device = Dev("psu1");
        var query = ScpiQuery.From("*IDN?").ShouldBeOk();

        await backend.OpenAsync(device, default);
        var result = await backend.QueryAsync(device, query, default);

        result.ShouldBeOk().ShouldBe("ACME,PSU,1,1.0");
        var queryEvent = sink.Events.Last();
        queryEvent.Op.ShouldBe(TrafficOp.Query);
        queryEvent.Data.ShouldBe("*IDN?");
        queryEvent.Response.ShouldBe("ACME,PSU,1,1.0");
        queryEvent.LatencyMs.ShouldNotBeNull();
        queryEvent.Ok.ShouldBeTrue();
    }

    [Fact]
    public async Task CloseAsync_emits_one_Close_event()
    {
        var sink = new RecordingWriter();
        var backend = new CapturingBackend(new FakeBackend(), sink);
        var device = Dev("psu1");

        await backend.OpenAsync(device, default);
        await backend.CloseAsync(device, default);

        sink.Events.Last().Op.ShouldBe(TrafficOp.Close);
    }

    [Fact]
    public async Task Sink_throwing_does_not_break_the_verb()
    {
        var sink = new ThrowingWriter();
        var backend = new CapturingBackend(new FakeBackend(), sink);
        var device = Dev("psu1");

        // Must NOT throw — the operator's traffic should never fail because
        // the capture sink failed.
        await Should.NotThrowAsync(() => backend.OpenAsync(device, default));
    }

    [Fact]
    public async Task Sink_throwing_OperationCanceledException_propagates()
    {
        var sink = new CancellingWriter();
        var backend = new CapturingBackend(new FakeBackend(), sink);
        var device = Dev("psu1");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Cancellation must be allowed to surface so the verb's loop can
        // tear down cleanly.
        await Should.ThrowAsync<OperationCanceledException>(() =>
            backend.OpenAsync(device, cts.Token)
        );
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

    private sealed class ThrowingWriter : ITrafficWriter
    {
        public Task AppendAsync(TrafficEvent ev, CancellationToken ct) =>
            throw new IOException("disk full");
    }

    private sealed class CancellingWriter : ITrafficWriter
    {
        public Task AppendAsync(TrafficEvent ev, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
