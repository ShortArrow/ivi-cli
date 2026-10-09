using System.Runtime.CompilerServices;
using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace IviCli.Application.Tests.Backends;

public sealed class DeviceTimeoutBackendFactoryTests
{
    private static readonly ScpiQuery Idn = ScpiQuery.From("*IDN?").ShouldBeOk();

    /// <summary>Real time a test waits for an operation before failing instead of hanging.</summary>
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    private static Device Dev(int timeoutMs) =>
        new(
            DeviceName.From("dut").ShouldBeOk(),
            VisaResource.Parse("TCPIP0::127.0.0.1::5025::SOCKET").ShouldBeOk(),
            Timeout.FromMilliseconds(timeoutMs).ShouldBeOk()
        );

    private static (IIviBackend Backend, StallingBackend Inner, FakeTimeProvider Time) Build(
        Device device,
        bool enforcesTimeout = false
    )
    {
        var inner = enforcesTimeout ? new EnforcingStallingBackend() : new StallingBackend();
        var time = new FakeTimeProvider();
        var factory = new DeviceTimeoutBackendFactory(new FakeBackendFactory(inner), time);
        return (factory.CreateFor(device).ShouldBeOk(), inner, time);
    }

    [Fact]
    public async Task A_query_that_outlasts_the_device_timeout_fails_with_TransportTimeout()
    {
        var device = Dev(300);
        var (backend, inner, time) = Build(device);
        inner.StallQueries = true;

        var pending = backend.QueryAsync(device, Idn, CancellationToken.None);
        time.Advance(TimeSpan.FromMilliseconds(299));
        pending.IsCompleted.ShouldBeFalse();
        time.Advance(TimeSpan.FromMilliseconds(1));
        var result = await pending.WaitAsync(Guard);

        result
            .ShouldBeOfType<Result<string, BackendError>.Error>()
            .Err.ShouldBeOfType<TransportTimeout>()
            .Elapsed.ShouldBe(TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public async Task A_timed_out_session_is_closed_and_reopened_before_the_next_operation()
    {
        var device = Dev(300);
        var (backend, inner, time) = Build(device);
        inner.StallQueries = true;
        var pending = backend.QueryAsync(device, Idn, CancellationToken.None);
        time.Advance(TimeSpan.FromMilliseconds(300));
        await pending.WaitAsync(Guard);
        inner.Calls.ShouldBe(["query", "close"]);

        inner.StallQueries = false;
        (
            await backend.QueryAsync(device, Idn, CancellationToken.None).WaitAsync(Guard)
        ).ShouldBeOk();

        inner.Calls.ShouldBe(["query", "close", "open", "query"]);
    }

    [Fact]
    public async Task Closing_a_timed_out_session_is_bounded_too()
    {
        var device = Dev(300);
        var (backend, inner, time) = Build(device);
        inner.StallQueries = true;
        inner.StallCloses = true;

        var pending = backend.QueryAsync(device, Idn, CancellationToken.None);
        time.Advance(TimeSpan.FromMilliseconds(300));
        await Task.Delay(50);
        pending.IsCompleted.ShouldBeFalse();
        time.Advance(TimeSpan.FromMilliseconds(300));

        (await pending.WaitAsync(Guard))
            .ShouldBeOfType<Result<string, BackendError>.Error>()
            .Err.ShouldBeOfType<TransportTimeout>()
            .Elapsed.ShouldBe(TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public async Task A_caller_cancellation_is_not_reported_as_a_timeout()
    {
        var device = Dev(300);
        var (backend, inner, _) = Build(device);
        inner.StallQueries = true;
        using var cts = new CancellationTokenSource();

        var pending = backend.QueryAsync(device, Idn, cts.Token);
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => pending.WaitAsync(Guard));
        inner.Calls.ShouldBe(["query"]);
    }

    [Fact]
    public async Task An_operation_that_finishes_in_time_returns_the_backends_result()
    {
        var device = Dev(300);
        var (backend, inner, _) = Build(device);

        (await backend.QueryAsync(device, Idn, CancellationToken.None).WaitAsync(Guard))
            .ShouldBeOk()
            .ShouldBe("ok");
        inner.Calls.ShouldBe(["query"]);
    }

    [Fact]
    public async Task A_write_that_outlasts_the_device_timeout_fails_with_TransportTimeout()
    {
        var device = Dev(300);
        var (backend, inner, time) = Build(device);
        inner.StallWrites = true;

        var pending = backend.WriteAsync(
            device,
            ScpiCommand.From("*RST").ShouldBeOk(),
            CancellationToken.None
        );
        time.Advance(TimeSpan.FromMilliseconds(300));

        (await pending.WaitAsync(Guard))
            .ShouldBeOfType<Result<Unit, BackendError>.Error>()
            .Err.ShouldBeOfType<TransportTimeout>();
    }

    [Fact]
    public async Task A_backend_that_enforces_the_timeout_itself_gets_a_second_of_grace()
    {
        var device = Dev(300);
        var (backend, inner, time) = Build(device, enforcesTimeout: true);
        inner.StallQueries = true;

        var pending = backend.QueryAsync(device, Idn, CancellationToken.None);
        time.Advance(TimeSpan.FromMilliseconds(1299));
        pending.IsCompleted.ShouldBeFalse();
        time.Advance(TimeSpan.FromMilliseconds(1));

        (await pending.WaitAsync(Guard))
            .ShouldBeOfType<Result<string, BackendError>.Error>()
            .Err.ShouldBeOfType<TransportTimeout>();
    }

    [Fact]
    public async Task Opening_waits_at_least_five_seconds_even_with_a_shorter_device_timeout()
    {
        var device = Dev(300);
        var (backend, inner, time) = Build(device);
        inner.StallOpens = true;

        var pending = backend.OpenAsync(device, CancellationToken.None);
        time.Advance(TimeSpan.FromMilliseconds(4999));
        pending.IsCompleted.ShouldBeFalse();
        time.Advance(TimeSpan.FromMilliseconds(1));

        (await pending.WaitAsync(Guard))
            .ShouldBeOfType<Result<Unit, BackendError>.Error>()
            .Err.ShouldBeOfType<TransportTimeout>();
    }

    [Fact]
    public async Task Opening_waits_for_the_device_timeout_when_it_is_longer_than_five_seconds()
    {
        var device = Dev(8000);
        var (backend, inner, time) = Build(device);
        inner.StallOpens = true;

        var pending = backend.OpenAsync(device, CancellationToken.None);
        time.Advance(TimeSpan.FromMilliseconds(7999));
        pending.IsCompleted.ShouldBeFalse();
        time.Advance(TimeSpan.FromMilliseconds(1));

        (await pending.WaitAsync(Guard))
            .ShouldBeOfType<Result<Unit, BackendError>.Error>()
            .Err.ShouldBeOfType<TransportTimeout>();
    }

    /// <summary>
    /// Records every call; an operation flagged to stall waits until its
    /// token is cancelled.
    /// </summary>
    private class StallingBackend : IIviBackend
    {
        public List<string> Calls { get; } = [];
        public bool StallOpens { get; set; }
        public bool StallQueries { get; set; }
        public bool StallWrites { get; set; }
        public bool StallCloses { get; set; }

        public async Task<Result<Unit, BackendError>> OpenAsync(Device device, CancellationToken ct)
        {
            Calls.Add("open");
            await StallIf(StallOpens, ct);
            return Result.Success<Unit, BackendError>(Unit.Value);
        }

        public async Task<Result<Unit, BackendError>> CloseAsync(
            Device device,
            CancellationToken ct
        )
        {
            Calls.Add("close");
            await StallIf(StallCloses, ct);
            return Result.Success<Unit, BackendError>(Unit.Value);
        }

        public async Task<Result<Unit, BackendError>> WriteAsync(
            Device device,
            ScpiCommand command,
            CancellationToken ct
        )
        {
            Calls.Add("write");
            await StallIf(StallWrites, ct);
            return Result.Success<Unit, BackendError>(Unit.Value);
        }

        public async Task<Result<string, BackendError>> QueryAsync(
            Device device,
            ScpiQuery query,
            CancellationToken ct
        )
        {
            Calls.Add("query");
            await StallIf(StallQueries, ct);
            return Result.Success<string, BackendError>("ok");
        }

        public Task<Result<string, BackendError>> ReadAsync(Device device, CancellationToken ct) =>
            Task.FromResult(Result.Success<string, BackendError>("ok"));

        public Task<Result<Unit, BackendError>> TriggerAsync(Device device, CancellationToken ct) =>
            Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

        public async IAsyncEnumerable<ServiceRequest> ServiceRequestStream(
            Device device,
            [EnumeratorCancellation] CancellationToken ct
        )
        {
            await Task.Yield();
            yield break;
        }

        private static async Task StallIf(bool stall, CancellationToken ct)
        {
            if (stall)
            {
                await Task.Delay(System.Threading.Timeout.Infinite, ct);
            }
        }
    }

    private sealed class EnforcingStallingBackend : StallingBackend, IEnforcesDeviceTimeout { }
}
