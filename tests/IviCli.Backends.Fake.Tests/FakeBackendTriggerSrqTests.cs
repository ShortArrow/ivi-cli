using IviCli.Application.Backends;
using IviCli.Backends.Fake;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Backends.Fake.Tests;

public sealed class FakeBackendTriggerSrqTests
{
    private static Device Dev() =>
        new(
            DeviceName.From("dut").ShouldBeOk(),
            VisaResource.Parse("TCPIP0::127.0.0.1::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(1000).ShouldBeOk()
        );

    [Fact]
    public async Task TriggerAsync_increments_TriggerCountFor()
    {
        var fake = new FakeBackend();
        (await fake.TriggerAsync(Dev(), default)).ShouldBeOk();
        (await fake.TriggerAsync(Dev(), default)).ShouldBeOk();

        fake.TriggerCountFor(Dev().Name).ShouldBe(2);
    }

    [Fact]
    public async Task ServiceRequestStream_yields_each_raised_event_then_completes_on_cancel()
    {
        var fake = new FakeBackend();
        using var cts = new CancellationTokenSource();
        var observed = new List<ServiceRequest>();
        var consumer = Task.Run(async () =>
        {
            await foreach (var srq in fake.ServiceRequestStream(Dev(), cts.Token))
            {
                observed.Add(srq);
                if (observed.Count >= 2)
                {
                    cts.Cancel();
                }
            }
        });

        fake.RaiseServiceRequest(Dev().Name, statusByte: 0x41);
        fake.RaiseServiceRequest(Dev().Name, statusByte: 0x42);

        try
        {
            await consumer.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (TaskCanceledException)
        { /* expected — Cancel() trips the loop */
        }
        catch (OperationCanceledException)
        { /* expected */
        }

        observed.Count.ShouldBe(2);
        observed[0].StatusByte.ShouldBe<byte>(0x41);
        observed[0].Device.Value.ShouldBe("dut");
        observed[1].StatusByte.ShouldBe<byte>(0x42);
    }

    [Fact]
    public async Task ServiceRequestStream_empty_until_event()
    {
        var fake = new FakeBackend();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var count = 0;
        try
        {
            await foreach (var _ in fake.ServiceRequestStream(Dev(), cts.Token))
            {
                count++;
            }
        }
        catch (OperationCanceledException) { }
        count.ShouldBe(0);
    }
}
