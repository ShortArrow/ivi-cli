using System.Diagnostics.Metrics;
using IviCli.Application.Backends;
using IviCli.Backends.Fake;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace IviCli.Application.Tests.Backends;

public sealed class PoolMetricsTests
{
    private static Device Dev(string name, int timeoutMs = 50) =>
        new(
            DeviceName.From(name).ShouldBeOk(),
            VisaResource.Parse("TCPIP0::127.0.0.1::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(timeoutMs).ShouldBeOk()
        );

    [Fact]
    public async Task PoolWaitTimeout_increments_lease_wait_timeouts_counter()
    {
        using var listener = new CounterRecorder("ivi.pool.lease_wait_timeouts");
        var time = new FakeTimeProvider();
        var pool = new PoolingBackendFactory(
            new FakeBackendFactory(new FakeBackend()),
            PoolConfig.Default,
            time
        );

        var holder = pool.CreateFor(Dev("dut", 5000)).ShouldBeOk();
        await holder.OpenAsync(Dev("dut", 5000), default);

        var second = pool.CreateFor(Dev("dut", 30)).ShouldBeOk();
        var result = await second.OpenAsync(Dev("dut", 30), default);
        result.ShouldBeError().ShouldBeOfType<PoolWaitTimeout>();

        // Counter is process-wide; a concurrently-running test may also
        // emit. Verify the lower bound — this test's lease timeout fired.
        listener.Counts.Sum().ShouldBeGreaterThanOrEqualTo(1);
        await pool.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_evictions_increment_evictions_counter()
    {
        using var listener = new CounterRecorder("ivi.pool.evictions");
        var time = new FakeTimeProvider();
        var pool = new PoolingBackendFactory(
            new FakeBackendFactory(new FakeBackend()),
            PoolConfig.Default,
            time
        );

        // No automatic eviction here because DisposeAsync closes entries
        // directly via _entries.Clear() — we instead drive eviction
        // through MaxDevices=1 + a 2nd device.
        var smallPool = PoolConfig.From(true, TimeSpan.FromSeconds(60), 1).ShouldBeOk();
        await pool.DisposeAsync();
        await using var lru = new PoolingBackendFactory(
            new FakeBackendFactory(new FakeBackend()),
            smallPool,
            time
        );
        var a = lru.CreateFor(Dev("a")).ShouldBeOk();
        await a.OpenAsync(Dev("a"), default);
        await a.CloseAsync(Dev("a"), default);
        var b = lru.CreateFor(Dev("b")).ShouldBeOk();
        await b.OpenAsync(Dev("b"), default);
        await b.CloseAsync(Dev("b"), default);

        await WaitForAsync(() => listener.Counts.Sum() >= 1);
        listener.Counts.Sum().ShouldBeGreaterThanOrEqualTo(1);
    }

    private static async Task WaitForAsync(Func<bool> predicate, int timeoutMs = 1000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }
            await Task.Delay(10);
        }
    }

    private sealed class CounterRecorder : IDisposable
    {
        public List<long> Counts { get; } = new();
        private readonly MeterListener _listener;

        public CounterRecorder(string name)
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, l) =>
                {
                    if (instrument.Name == name)
                    {
                        l.EnableMeasurementEvents(instrument);
                    }
                },
            };
            _listener.SetMeasurementEventCallback<long>(
                (instrument, value, tags, state) => Counts.Add(value)
            );
            _listener.Start();
        }

        public void Dispose() => _listener.Dispose();
    }
}
