using System.Collections.Immutable;
using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Backends.Fake.Tests;

/// <summary>
/// Behaviour tests for the SRQ notify-wedge quirk of issue #115: once
/// the bound scenario's threshold of stream deliveries is reached the
/// mock keeps recording the status byte but stops notifying, and only
/// reopening the device recovers it.
/// </summary>
public sealed class FakeBackendQuirkTests
{
    private static Device Dev() =>
        new(
            DeviceName.From("dut").ShouldBeOk(),
            VisaResource.Parse("TCPIP0::127.0.0.1::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(1000).ShouldBeOk()
        );

    private static MockScenario WedgeAfter(int deliveries) =>
        MockScenario.SingleScene(
            ScenarioName.From("wedge").ShouldBeOk(),
            idnDefault: null,
            rules: ImmutableArray<MockRule>.Empty
        ) with
        {
            Quirks = new MockQuirks(SrqNotifyWedgeAfter: deliveries),
        };

    private static async Task<List<ServiceRequest>> DrainAsync(FakeBackend fake, int forMs = 150)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(forMs));
        var observed = new List<ServiceRequest>();
        try
        {
            await foreach (var srq in fake.ServiceRequestStream(Dev(), cts.Token))
            {
                observed.Add(srq);
            }
        }
        catch (OperationCanceledException) { }
        return observed;
    }

    [Fact]
    public async Task Without_quirks_every_raise_reaches_the_stream()
    {
        var fake = new FakeBackend();
        fake.RaiseServiceRequest(Dev().Name, statusByte: 0x41);
        fake.RaiseServiceRequest(Dev().Name, statusByte: 0x42);

        (await DrainAsync(fake)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Wedge_after_one_delivery_silences_the_stream_but_not_the_status_byte()
    {
        var fake = new FakeBackend();
        fake.ActivateScenario(WedgeAfter(1), Dev().Name);

        fake.RaiseServiceRequest(Dev().Name, statusByte: 0x41);
        fake.RaiseServiceRequest(Dev().Name, statusByte: 0x42);
        fake.RaiseServiceRequest(Dev().Name, statusByte: 0x44);

        var observed = await DrainAsync(fake);
        observed.Count.ShouldBe(1);
        observed[0].StatusByte.ShouldBe<byte>(0x41);
        fake.LastStatusByteFor(Dev().Name).ShouldBe<byte>(0x44);
    }

    [Fact]
    public async Task Wedge_after_zero_deliveries_never_notifies()
    {
        var fake = new FakeBackend();
        fake.ActivateScenario(WedgeAfter(0), Dev().Name);

        fake.RaiseServiceRequest(Dev().Name, statusByte: 0x41);

        (await DrainAsync(fake)).ShouldBeEmpty();
        fake.LastStatusByteFor(Dev().Name).ShouldBe<byte>(0x41);
    }

    [Fact]
    public async Task Reopening_the_device_recovers_a_wedged_stream()
    {
        var fake = new FakeBackend();
        fake.ActivateScenario(WedgeAfter(1), Dev().Name);

        fake.RaiseServiceRequest(Dev().Name, statusByte: 0x41);
        fake.RaiseServiceRequest(Dev().Name, statusByte: 0x42);
        (await DrainAsync(fake)).Count.ShouldBe(1);

        (await fake.CloseAsync(Dev(), default)).ShouldBeOk();
        (await fake.OpenAsync(Dev(), default)).ShouldBeOk();
        fake.RaiseServiceRequest(Dev().Name, statusByte: 0x43);

        var observed = await DrainAsync(fake);
        observed.Count.ShouldBe(1);
        observed[0].StatusByte.ShouldBe<byte>(0x43);
    }

    [Fact]
    public async Task The_wedge_only_binds_the_device_its_scenario_is_active_on()
    {
        var other = DeviceName.From("other").ShouldBeOk();
        var fake = new FakeBackend();
        fake.ActivateScenario(WedgeAfter(0), other);

        fake.RaiseServiceRequest(Dev().Name, statusByte: 0x41);

        (await DrainAsync(fake)).Count.ShouldBe(1);
    }
}
