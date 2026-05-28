using System.Diagnostics;
using System.Diagnostics.Metrics;
using IviCli.Application.Backends;
using IviCli.Application.Telemetry;
using IviCli.Backends.Fake;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Application.Tests.Backends;

public sealed class InstrumentingBackendFactoryTests
{
    private static Device Dev() =>
        new(
            DeviceName.From("dut").ShouldBeOk(),
            VisaResource.Parse("TCPIP0::127.0.0.1::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(1000).ShouldBeOk()
        );

    [Fact]
    public async Task QueryAsync_emits_one_activity_with_tags()
    {
        using var listener = new RecordingActivityListener(IviCliTelemetry.Backend.Name);
        var fake = new FakeBackend().RespondToQuery(Dev().Name, "*IDN?", "FAKE,FAKE,0,1.0");
        var factory = new InstrumentingBackendFactory(new FakeBackendFactory(fake));

        var backend = factory.CreateFor(Dev()).ShouldBeOk();
        await backend.OpenAsync(Dev(), default);
        await backend.QueryAsync(Dev(), ScpiQuery.From("*IDN?").ShouldBeOk(), default);
        await backend.CloseAsync(Dev(), default);

        listener.Activities.Count.ShouldBe(3);
        var query = listener.Activities.Single(a => a.OperationName == "backend.query");
        query.GetTagItem("ivi.device").ShouldBe("dut");
        query.GetTagItem("scpi.text").ShouldBe("*IDN?");
        query.GetTagItem("outcome").ShouldBe("ok");
    }

    [Fact]
    public async Task QueryAsync_sets_activity_error_status_on_BackendError()
    {
        using var listener = new RecordingActivityListener(IviCliTelemetry.Backend.Name);
        var fake = new FakeBackend().FailQuery(
            Dev().Name,
            "*IDN?",
            new TransportDisconnected("boom")
        );
        var factory = new InstrumentingBackendFactory(new FakeBackendFactory(fake));

        var backend = factory.CreateFor(Dev()).ShouldBeOk();
        await backend.OpenAsync(Dev(), default);
        await backend.QueryAsync(Dev(), ScpiQuery.From("*IDN?").ShouldBeOk(), default);

        var query = listener.Activities.Single(a => a.OperationName == "backend.query");
        query.Status.ShouldBe(ActivityStatusCode.Error);
        query.GetTagItem("outcome").ShouldBe("error");
    }

    [Fact]
    public async Task BackendOpDurationMs_histogram_records_each_op()
    {
        using var meterListener = new RecordingMeterListener("ivi.backend.op_duration");
        var fake = new FakeBackend().RespondToQuery(Dev().Name, "*IDN?", "FAKE,FAKE,0,1.0");
        var factory = new InstrumentingBackendFactory(new FakeBackendFactory(fake));

        var backend = factory.CreateFor(Dev()).ShouldBeOk();
        await backend.OpenAsync(Dev(), default);
        await backend.QueryAsync(Dev(), ScpiQuery.From("*IDN?").ShouldBeOk(), default);
        await backend.CloseAsync(Dev(), default);

        meterListener.Measurements.Count.ShouldBe(3);
        meterListener.Measurements.ShouldContain(m => OpTag(m) == "open");
        meterListener.Measurements.ShouldContain(m => OpTag(m) == "query");
        meterListener.Measurements.ShouldContain(m => OpTag(m) == "close");
    }

    private static string? OpTag(RecordedMeasurement m) =>
        m.Tags.TryGetValue("op", out var v) ? v?.ToString() : null;

    private sealed class RecordingActivityListener : IDisposable
    {
        public List<Activity> Activities { get; } = new();
        private readonly ActivityListener _listener;

        public RecordingActivityListener(string sourceName)
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = src => src.Name == sourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = a => Activities.Add(a),
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public void Dispose() => _listener.Dispose();
    }

    private sealed class RecordingMeterListener : IDisposable
    {
        public List<RecordedMeasurement> Measurements { get; } = new();
        private readonly MeterListener _listener;

        public RecordingMeterListener(string instrumentName)
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, l) =>
                {
                    if (instrument.Name == instrumentName)
                    {
                        l.EnableMeasurementEvents(instrument);
                    }
                },
            };
            _listener.SetMeasurementEventCallback<double>(
                (instrument, value, tags, state) =>
                {
                    var tagMap = new Dictionary<string, object?>();
                    for (var i = 0; i < tags.Length; i++)
                    {
                        tagMap[tags[i].Key] = tags[i].Value;
                    }
                    Measurements.Add(new RecordedMeasurement(value, tagMap));
                }
            );
            _listener.Start();
        }

        public void Dispose() => _listener.Dispose();
    }

    public sealed record RecordedMeasurement(
        double Value,
        IReadOnlyDictionary<string, object?> Tags
    );
}
