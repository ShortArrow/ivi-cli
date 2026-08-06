using IviCli.Application.Backends;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Backends.Local.Tests;

/// <summary>
/// Local backend SRQ delivery (ADR 0041): the VISA session's service
/// request callback becomes a <see cref="ServiceRequest"/> stream, and
/// every failure mode degrades to an empty stream rather than throwing.
/// </summary>
public class LocalBackendServiceRequestTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private static Device Dev(string name = "psu") =>
        new(
            DeviceName.From(name).ShouldBeOk(),
            VisaResource.Parse("USB0::0x0699::0x0408::C012345::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    private static async Task<(LocalBackend Backend, FakeVisaSession Session)> OpenedAsync(
        string name = "psu"
    )
    {
        var factory = new FakeVisaSessionFactory();
        var session = new FakeVisaSession();
        factory.Sessions[VisaResourceFormatter.Format(Dev(name).Resource)] = session;
        var backend = new LocalBackend(factory);
        (await backend.OpenAsync(Dev(name), default)).ShouldBeOk();
        return (backend, session);
    }

    private static Task<List<ServiceRequest>> ConsumeAsync(
        LocalBackend backend,
        Device device,
        int take,
        CancellationToken ct
    ) =>
        Task.Run(async () =>
        {
            var observed = new List<ServiceRequest>();
            await foreach (var srq in backend.ServiceRequestStream(device, ct))
            {
                observed.Add(srq);
                if (observed.Count >= take)
                {
                    break;
                }
            }
            return observed;
        });

    [Fact]
    public async Task A_raised_service_request_surfaces_with_device_and_status_byte()
    {
        // Given an open session whose stream is being consumed
        var (backend, session) = await OpenedAsync();
        var consumer = ConsumeAsync(backend, Dev(), take: 1, CancellationToken.None);
        await session.ServiceRequestsEnabled.WaitAsync(Patience);

        // When the instrument raises an SRQ with status byte 0x42
        session.RaiseServiceRequest(0x42);

        // Then one ServiceRequest carries that device and status byte
        var observed = await consumer.WaitAsync(Patience);
        observed.Count.ShouldBe(1);
        observed[0].Device.Value.ShouldBe("psu");
        observed[0].StatusByte.ShouldBe<byte>(0x42);
    }

    [Fact]
    public async Task Consecutive_service_requests_arrive_in_order()
    {
        var (backend, session) = await OpenedAsync();
        var consumer = ConsumeAsync(backend, Dev(), take: 2, CancellationToken.None);
        await session.ServiceRequestsEnabled.WaitAsync(Patience);

        session.RaiseServiceRequest(0x41);
        session.RaiseServiceRequest(0x42);

        var observed = await consumer.WaitAsync(Patience);
        observed.Count.ShouldBe(2);
        observed[0].StatusByte.ShouldBe<byte>(0x41);
        observed[1].StatusByte.ShouldBe<byte>(0x42);
    }

    [Fact]
    public async Task Cancellation_ends_the_stream_without_throwing()
    {
        var (backend, session) = await OpenedAsync();
        using var cts = new CancellationTokenSource();
        var consumer = Task.Run(async () =>
        {
            await foreach (var _ in backend.ServiceRequestStream(Dev(), cts.Token)) { }
        });
        await session.ServiceRequestsEnabled.WaitAsync(Patience);

        await cts.CancelAsync();

        await Should.NotThrowAsync(() => consumer.WaitAsync(Patience));
    }

    [Fact]
    public async Task Stream_for_a_device_that_was_never_opened_completes_empty()
    {
        var backend = new LocalBackend(new FakeVisaSessionFactory());

        var observed = new List<ServiceRequest>();
        await foreach (var srq in backend.ServiceRequestStream(Dev(), CancellationToken.None))
        {
            observed.Add(srq);
        }

        observed.ShouldBeEmpty();
    }

    [Fact]
    public async Task Stream_completes_empty_when_the_session_refuses_to_enable_service_requests()
    {
        var (backend, session) = await OpenedAsync();
        session.FailServiceRequestEnable = true;

        var observed = new List<ServiceRequest>();
        await foreach (var srq in backend.ServiceRequestStream(Dev(), CancellationToken.None))
        {
            observed.Add(srq);
        }

        observed.ShouldBeEmpty();
    }
}
