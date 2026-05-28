using IviCli.Application.Backends;
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

public sealed class PoolingBackendFactoryTests
{
    private static Device Dev(string name, int timeoutMs = 1000) =>
        new(
            DeviceName.From(name).ShouldBeOk(),
            VisaResource.Parse("TCPIP0::127.0.0.1::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(timeoutMs).ShouldBeOk()
        );

    private static PoolConfig DefaultPool(
        TimeSpan? idle = null,
        int? max = null,
        bool enabled = true
    ) => PoolConfig.From(enabled, idle ?? TimeSpan.FromSeconds(60), max ?? 16).ShouldBeOk();

    [Fact]
    public async Task Sequential_leases_to_same_device_reuse_one_open()
    {
        var fake = new FakeBackend().RespondToQuery(Dev("dut").Name, "*IDN?", "FAKE,FAKE,0,1.0");
        var time = new FakeTimeProvider();
        await using var pool = new PoolingBackendFactory(
            new FakeBackendFactory(fake),
            DefaultPool(),
            time
        );

        for (var i = 0; i < 2; i++)
        {
            var backend = pool.CreateFor(Dev("dut")).ShouldBeOk();
            (await backend.OpenAsync(Dev("dut"), default)).ShouldBeOk();
            (
                await backend.QueryAsync(Dev("dut"), ScpiQuery.From("*IDN?").ShouldBeOk(), default)
            ).ShouldBeOk();
            (await backend.CloseAsync(Dev("dut"), default)).ShouldBeOk();
        }

        fake.OpenCountFor(Dev("dut").Name).ShouldBe(1);
        fake.CloseCountFor(Dev("dut").Name).ShouldBe(0); // pool defers close
    }

    [Fact]
    public async Task Idle_eviction_after_timeout_closes_inner_backend()
    {
        var fake = new FakeBackend();
        var time = new FakeTimeProvider();
        await using var pool = new PoolingBackendFactory(
            new FakeBackendFactory(fake),
            DefaultPool(idle: TimeSpan.FromSeconds(10)),
            time
        );

        var backend = pool.CreateFor(Dev("dut")).ShouldBeOk();
        (await backend.OpenAsync(Dev("dut"), default)).ShouldBeOk();
        (await backend.CloseAsync(Dev("dut"), default)).ShouldBeOk();
        fake.CloseCountFor(Dev("dut").Name).ShouldBe(0);

        // Advance past idle — sweep + lazy eviction should close the entry.
        time.Advance(TimeSpan.FromSeconds(11));
        // Trigger sweep via a fresh lease attempt on a different device.
        var other = pool.CreateFor(Dev("other")).ShouldBeOk();
        (await other.OpenAsync(Dev("other"), default)).ShouldBeOk();
        (await other.CloseAsync(Dev("other"), default)).ShouldBeOk();
        // Eviction Close is fire-and-forget; allow the task to complete.
        await WaitForAsync(() => fake.CloseCountFor(Dev("dut").Name) == 1);
        fake.CloseCountFor(Dev("dut").Name).ShouldBe(1);

        // A subsequent open on dut should call inner.OpenAsync again.
        var backend2 = pool.CreateFor(Dev("dut")).ShouldBeOk();
        (await backend2.OpenAsync(Dev("dut"), default)).ShouldBeOk();
        fake.OpenCountFor(Dev("dut").Name).ShouldBe(2);
    }

    [Fact]
    public async Task Concurrent_leases_to_same_device_serialise()
    {
        var fake = new FakeBackend();
        var time = new FakeTimeProvider();
        await using var pool = new PoolingBackendFactory(
            new FakeBackendFactory(fake),
            DefaultPool(),
            time
        );

        var first = pool.CreateFor(Dev("dut", timeoutMs: 5000)).ShouldBeOk();
        (await first.OpenAsync(Dev("dut", timeoutMs: 5000), default)).ShouldBeOk();

        var secondReleased = new TaskCompletionSource();
        var secondTask = Task.Run(async () =>
        {
            var second = pool.CreateFor(Dev("dut", timeoutMs: 5000)).ShouldBeOk();
            (await second.OpenAsync(Dev("dut", timeoutMs: 5000), default)).ShouldBeOk();
            secondReleased.SetResult();
            await second.CloseAsync(Dev("dut", timeoutMs: 5000), default);
        });

        // Second must not have completed while first holds the lease.
        secondReleased.Task.IsCompleted.ShouldBeFalse();
        await first.CloseAsync(Dev("dut", timeoutMs: 5000), default);
        await secondTask.WaitAsync(TimeSpan.FromSeconds(3));
        secondReleased.Task.IsCompleted.ShouldBeTrue();

        fake.OpenCountFor(Dev("dut").Name).ShouldBe(1); // both reused the same entry
    }

    [Fact]
    public async Task Wait_timeout_returns_PoolWaitTimeout()
    {
        var fake = new FakeBackend();
        var time = new FakeTimeProvider();
        await using var pool = new PoolingBackendFactory(
            new FakeBackendFactory(fake),
            DefaultPool(),
            time
        );

        var holder = pool.CreateFor(Dev("dut", timeoutMs: 5000)).ShouldBeOk();
        (await holder.OpenAsync(Dev("dut", timeoutMs: 5000), default)).ShouldBeOk();

        var second = pool.CreateFor(Dev("dut", timeoutMs: 50)).ShouldBeOk();
        var openResult = await second.OpenAsync(Dev("dut", timeoutMs: 50), default);

        openResult.ShouldBeError().ShouldBeOfType<PoolWaitTimeout>();
    }

    [Fact]
    public async Task BackendError_during_op_evicts_entry()
    {
        var fake = new FakeBackend().FailQuery(
            Dev("dut").Name,
            "*IDN?",
            new TransportDisconnected("boom")
        );
        var time = new FakeTimeProvider();
        await using var pool = new PoolingBackendFactory(
            new FakeBackendFactory(fake),
            DefaultPool(),
            time
        );

        var backend = pool.CreateFor(Dev("dut")).ShouldBeOk();
        (await backend.OpenAsync(Dev("dut"), default)).ShouldBeOk();
        var queryResult = await backend.QueryAsync(
            Dev("dut"),
            ScpiQuery.From("*IDN?").ShouldBeOk(),
            default
        );
        queryResult.ShouldBeError();
        (await backend.CloseAsync(Dev("dut"), default)).ShouldBeOk();

        await WaitForAsync(() => fake.CloseCountFor(Dev("dut").Name) == 1);
        fake.CloseCountFor(Dev("dut").Name).ShouldBe(1);

        // Re-opening should issue a fresh inner.OpenAsync.
        var backend2 = pool.CreateFor(Dev("dut")).ShouldBeOk();
        (await backend2.OpenAsync(Dev("dut"), default)).ShouldBeOk();
        fake.OpenCountFor(Dev("dut").Name).ShouldBe(2);
    }

    [Fact]
    public async Task Max_devices_LRU_eviction_closes_oldest()
    {
        var fake = new FakeBackend();
        var time = new FakeTimeProvider();
        await using var pool = new PoolingBackendFactory(
            new FakeBackendFactory(fake),
            DefaultPool(max: 2),
            time
        );

        await OpenCloseAsync(pool, Dev("a"));
        time.Advance(TimeSpan.FromSeconds(1));
        await OpenCloseAsync(pool, Dev("b"));
        time.Advance(TimeSpan.FromSeconds(1));
        // Third device: 'a' is LRU and should be evicted.
        await OpenCloseAsync(pool, Dev("c"));

        await WaitForAsync(() => fake.CloseCountFor(Dev("a").Name) == 1);
        fake.CloseCountFor(Dev("a").Name).ShouldBe(1);
        fake.CloseCountFor(Dev("b").Name).ShouldBe(0);
        fake.CloseCountFor(Dev("c").Name).ShouldBe(0);
    }

    [Fact]
    public async Task DisposeAsync_closes_all_entries()
    {
        var fake = new FakeBackend();
        var time = new FakeTimeProvider();
        var pool = new PoolingBackendFactory(new FakeBackendFactory(fake), DefaultPool(), time);

        await OpenCloseAsync(pool, Dev("a"));
        await OpenCloseAsync(pool, Dev("b"));
        fake.CloseCountFor(Dev("a").Name).ShouldBe(0);

        await pool.DisposeAsync();

        fake.CloseCountFor(Dev("a").Name).ShouldBe(1);
        fake.CloseCountFor(Dev("b").Name).ShouldBe(1);
    }

    [Fact]
    public async Task Two_devices_share_pool_with_independent_entries()
    {
        var fake = new FakeBackend();
        var time = new FakeTimeProvider();
        await using var pool = new PoolingBackendFactory(
            new FakeBackendFactory(fake),
            DefaultPool(),
            time
        );

        await OpenCloseAsync(pool, Dev("a"));
        await OpenCloseAsync(pool, Dev("b"));

        pool.CachedEntryCount.ShouldBe(2);
        fake.OpenCountFor(Dev("a").Name).ShouldBe(1);
        fake.OpenCountFor(Dev("b").Name).ShouldBe(1);
    }

    private static async Task OpenCloseAsync(PoolingBackendFactory pool, Device device)
    {
        var backend = pool.CreateFor(device).ShouldBeOk();
        (await backend.OpenAsync(device, default)).ShouldBeOk();
        (await backend.CloseAsync(device, default)).ShouldBeOk();
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
}
