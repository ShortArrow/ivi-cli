using System.Runtime.CompilerServices;
using System.Threading.Channels;
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

    [Fact]
    public async Task A_read_that_outlasts_the_device_timeout_fails_with_TransportTimeout()
    {
        var device = Dev(300);
        var (backend, inner, time) = Build(device);
        inner.StallReads = true;

        var pending = backend.ReadAsync(device, CancellationToken.None);
        time.Advance(TimeSpan.FromMilliseconds(300));

        (await pending.WaitAsync(Guard))
            .ShouldBeOfType<Result<string, BackendError>.Error>()
            .Err.ShouldBeOfType<TransportTimeout>();
    }

    [Fact]
    public async Task A_trigger_that_outlasts_the_device_timeout_fails_with_TransportTimeout()
    {
        var device = Dev(300);
        var (backend, inner, time) = Build(device);
        inner.StallTriggers = true;

        var pending = backend.TriggerAsync(device, CancellationToken.None);
        time.Advance(TimeSpan.FromMilliseconds(300));

        (await pending.WaitAsync(Guard))
            .ShouldBeOfType<Result<Unit, BackendError>.Error>()
            .Err.ShouldBeOfType<TransportTimeout>();
    }

    [Fact]
    public async Task A_timeout_the_backend_reports_itself_also_reopens_the_session()
    {
        var device = Dev(300);
        var (backend, inner, _) = Build(device, enforcesTimeout: true);
        var reported = new TransportTimeout(TimeSpan.FromMilliseconds(300));
        inner.QueryFailure = reported;

        (await backend.QueryAsync(device, Idn, CancellationToken.None).WaitAsync(Guard))
            .ShouldBeOfType<Result<string, BackendError>.Error>()
            .Err.ShouldBeSameAs(reported);
        inner.QueryFailure = null;
        (
            await backend.QueryAsync(device, Idn, CancellationToken.None).WaitAsync(Guard)
        ).ShouldBeOk();

        inner.Calls.ShouldBe(["query", "close", "open", "query"]);
    }

    [Fact]
    public async Task A_failure_other_than_a_timeout_keeps_the_session()
    {
        var device = Dev(300);
        var (backend, inner, _) = Build(device);
        inner.QueryFailure = new TransportDisconnected("gone");

        await backend.QueryAsync(device, Idn, CancellationToken.None).WaitAsync(Guard);
        inner.QueryFailure = null;
        await backend.QueryAsync(device, Idn, CancellationToken.None).WaitAsync(Guard);

        inner.Calls.ShouldBe(["query", "query"]);
    }

    [Fact]
    public async Task Service_requests_keep_arriving_after_a_timed_out_session_is_reopened()
    {
        var device = Dev(300);
        var (backend, inner, time) = Build(device);
        using var cts = new CancellationTokenSource(Guard);
        var received = Channel.CreateUnbounded<ServiceRequest>();
        var listening = Listen(backend, device, received.Writer, cts.Token);
        await Until(() => inner.SrqSubscriptions == 1);

        inner.StallQueries = true;
        var pending = backend.QueryAsync(device, Idn, CancellationToken.None);
        time.Advance(TimeSpan.FromMilliseconds(300));
        await pending.WaitAsync(Guard);
        inner.StallQueries = false;
        (
            await backend.QueryAsync(device, Idn, CancellationToken.None).WaitAsync(Guard)
        ).ShouldBeOk();
        await Until(() => inner.SrqSubscriptions == 2);
        inner.RaiseServiceRequest(device, 0x60);

        (await received.Reader.ReadAsync(cts.Token)).StatusByte.ShouldBe((byte)0x60);
        listening.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task Service_requests_end_when_the_caller_closes_the_session()
    {
        var device = Dev(300);
        var (backend, inner, _) = Build(device);
        using var cts = new CancellationTokenSource(Guard * 2);
        var received = Channel.CreateUnbounded<ServiceRequest>();
        var listening = Listen(backend, device, received.Writer, cts.Token);
        await Until(() => inner.SrqSubscriptions == 1);

        (await backend.CloseAsync(device, CancellationToken.None)).ShouldBeOk();

        await listening.WaitAsync(Guard);
        inner.SrqSubscriptions.ShouldBe(1);
    }

    [Fact]
    public async Task Service_requests_end_when_the_caller_closes_a_session_that_timed_out()
    {
        var device = Dev(300);
        var (backend, inner, time) = Build(device);
        using var cts = new CancellationTokenSource(Guard * 2);
        var received = Channel.CreateUnbounded<ServiceRequest>();
        var listening = Listen(backend, device, received.Writer, cts.Token);
        await Until(() => inner.SrqSubscriptions == 1);
        inner.StallQueries = true;
        var pending = backend.QueryAsync(device, Idn, CancellationToken.None);
        time.Advance(TimeSpan.FromMilliseconds(300));
        await pending.WaitAsync(Guard);

        (await backend.CloseAsync(device, CancellationToken.None)).ShouldBeOk();

        await listening.WaitAsync(Guard);
    }

    private static Task Listen(
        IIviBackend backend,
        Device device,
        ChannelWriter<ServiceRequest> sink,
        CancellationToken ct
    ) =>
        Task.Run(
            async () =>
            {
                await foreach (var srq in backend.ServiceRequestStream(device, ct))
                {
                    sink.TryWrite(srq);
                }
            },
            ct
        );

    private static async Task Until(Func<bool> condition)
    {
        var giveUp = DateTime.UtcNow + Guard;
        while (!condition())
        {
            DateTime.UtcNow.ShouldBeLessThan(giveUp);
            await Task.Delay(10);
        }
    }

    /// <summary>
    /// Records every call; an operation flagged to stall waits until its
    /// token is cancelled. Each open starts a fresh service-request stream
    /// and each close ends it, as a real session does.
    /// </summary>
    private class StallingBackend : IIviBackend
    {
        private Channel<ServiceRequest> _serviceRequests =
            Channel.CreateUnbounded<ServiceRequest>();
        private int _srqSubscriptions;

        public List<string> Calls { get; } = [];
        public bool StallOpens { get; set; }
        public bool StallQueries { get; set; }
        public bool StallWrites { get; set; }
        public bool StallReads { get; set; }
        public bool StallTriggers { get; set; }
        public bool StallCloses { get; set; }

        /// <summary>When set, a query returns this error instead of answering.</summary>
        public BackendError? QueryFailure { get; set; }

        public int SrqSubscriptions => Volatile.Read(ref _srqSubscriptions);

        /// <summary>Delivers a service request on the current session's stream.</summary>
        public void RaiseServiceRequest(Device device, byte statusByte) =>
            _serviceRequests.Writer.TryWrite(
                new ServiceRequest(device.Name, statusByte, DateTimeOffset.UtcNow)
            );

        public async Task<Result<Unit, BackendError>> OpenAsync(Device device, CancellationToken ct)
        {
            Calls.Add("open");
            await StallIf(StallOpens, ct);
            _serviceRequests = Channel.CreateUnbounded<ServiceRequest>();
            return Result.Success<Unit, BackendError>(Unit.Value);
        }

        public async Task<Result<Unit, BackendError>> CloseAsync(
            Device device,
            CancellationToken ct
        )
        {
            Calls.Add("close");
            _serviceRequests.Writer.TryComplete();
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
            return QueryFailure is { } failure
                ? Result.Failure<string, BackendError>(failure)
                : Result.Success<string, BackendError>("ok");
        }

        public async Task<Result<string, BackendError>> ReadAsync(
            Device device,
            CancellationToken ct
        )
        {
            Calls.Add("read");
            await StallIf(StallReads, ct);
            return Result.Success<string, BackendError>("ok");
        }

        public async Task<Result<Unit, BackendError>> TriggerAsync(
            Device device,
            CancellationToken ct
        )
        {
            Calls.Add("trigger");
            await StallIf(StallTriggers, ct);
            return Result.Success<Unit, BackendError>(Unit.Value);
        }

        public async IAsyncEnumerable<ServiceRequest> ServiceRequestStream(
            Device device,
            [EnumeratorCancellation] CancellationToken ct
        )
        {
            var reader = _serviceRequests.Reader;
            Interlocked.Increment(ref _srqSubscriptions);
            await foreach (var srq in reader.ReadAllAsync(ct))
            {
                yield return srq;
            }
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
