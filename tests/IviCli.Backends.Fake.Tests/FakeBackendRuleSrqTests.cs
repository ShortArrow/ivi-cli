using System.Collections.Immutable;
using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Backends.Fake.Tests;

/// <summary>
/// A scenario rule that carries <c>srq</c> raises one service request
/// each time it fires — whatever its action — reporting the status byte
/// verbatim and going out through the same path as a raise from a test,
/// so the scenario's quirks gate it too.
/// </summary>
public sealed class FakeBackendRuleSrqTests
{
    private static readonly SceneName Armed = SceneName.From("armed").ShouldBeOk();
    private static readonly SceneName Done = SceneName.From("done").ShouldBeOk();

    private static DeviceName DevName() => DeviceName.From("dut").ShouldBeOk();

    private static Device Dev() =>
        new(
            DevName(),
            VisaResource.Parse("TCPIP0::127.0.0.1::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(1000).ShouldBeOk()
        );

    private static MockScenario Scenario(params MockRule[] rules) =>
        MockScenario.SingleScene(
            ScenarioName.From("srq").ShouldBeOk(),
            idnDefault: null,
            rules: [.. rules]
        );

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
    public async Task Respond_rule_with_srq_answers_and_raises()
    {
        var fake = new FakeBackend().ActivateScenario(
            Scenario(new MockRule("MEAS?", new RuleAction.Respond("1.23"), Srq: 0x60)),
            DevName()
        );

        (await fake.QueryAsync(Dev(), ScpiQuery.From("MEAS?").ShouldBeOk(), default))
            .ShouldBeOk()
            .ShouldBe("1.23");

        var observed = await DrainAsync(fake);
        observed.ShouldHaveSingleItem().StatusByte.ShouldBe<byte>(0x60);
    }

    [Fact]
    public async Task Ack_rule_with_srq_raises()
    {
        var fake = new FakeBackend().ActivateScenario(
            Scenario(new MockRule("*OPC", new RuleAction.Ack(), Srq: 0x60)),
            DevName()
        );

        (await fake.WriteAsync(Dev(), ScpiCommand.From("*OPC").ShouldBeOk(), default)).ShouldBeOk();

        var observed = await DrainAsync(fake);
        observed.ShouldHaveSingleItem().StatusByte.ShouldBe<byte>(0x60);
    }

    [Fact]
    public async Task Fail_rule_with_srq_raises()
    {
        var fake = new FakeBackend().ActivateScenario(
            Scenario(
                new MockRule(
                    "INIT",
                    new RuleAction.Fail("transport_timeout", Detail: null),
                    Srq: 0x60
                )
            ),
            DevName()
        );

        (
            await fake.WriteAsync(Dev(), ScpiCommand.From("INIT").ShouldBeOk(), default)
        ).ShouldBeError();

        var observed = await DrainAsync(fake);
        observed.ShouldHaveSingleItem().StatusByte.ShouldBe<byte>(0x60);
    }

    [Fact]
    public async Task A_rule_without_srq_raises_nothing()
    {
        var fake = new FakeBackend().ActivateScenario(
            Scenario(new MockRule("*OPC", new RuleAction.Ack())),
            DevName()
        );

        (await fake.WriteAsync(Dev(), ScpiCommand.From("*OPC").ShouldBeOk(), default)).ShouldBeOk();

        (await DrainAsync(fake)).ShouldBeEmpty();
        fake.LastStatusByteFor(DevName()).ShouldBe<byte>(0);
    }

    [Fact]
    public async Task Srq_is_raised_after_the_transition_took_effect()
    {
        var fake = new FakeBackend();
        fake.ActivateScenario(
            new MockScenario(
                ScenarioName.From("srq-fsm").ShouldBeOk(),
                InitialScene: Armed,
                IdnDefault: null,
                Scenes: ImmutableArray.Create(
                    new MockScene(
                        Armed,
                        ImmutableArray.Create(
                            new MockRule("*OPC", new RuleAction.Ack(Transition: Done), Srq: 0x60)
                        )
                    ),
                    MockScene.Empty(Done)
                )
            ),
            DevName()
        );

        SceneName? sceneWhenObserved = null;
        using var consumerCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var consumer = Task.Run(async () =>
        {
            await foreach (var srq in fake.ServiceRequestStream(Dev(), consumerCts.Token))
            {
                sceneWhenObserved = fake.GetCurrentScene(DevName());
                return srq;
            }
            return null;
        });

        (await fake.WriteAsync(Dev(), ScpiCommand.From("*OPC").ShouldBeOk(), default)).ShouldBeOk();

        (await consumer).ShouldNotBeNull().StatusByte.ShouldBe<byte>(0x60);
        sceneWhenObserved.ShouldBe(Done);
    }

    [Fact]
    public async Task Rule_srq_is_gated_by_the_notify_wedge()
    {
        var fake = new FakeBackend().ActivateScenario(
            Scenario(
                new MockRule("FIRST", new RuleAction.Ack(), Srq: 0x41),
                new MockRule("SECOND", new RuleAction.Ack(), Srq: 0x44)
            ) with
            {
                Quirks = new MockQuirks(SrqNotifyWedgeAfter: 1),
            },
            DevName()
        );

        (
            await fake.WriteAsync(Dev(), ScpiCommand.From("FIRST").ShouldBeOk(), default)
        ).ShouldBeOk();
        (
            await fake.WriteAsync(Dev(), ScpiCommand.From("SECOND").ShouldBeOk(), default)
        ).ShouldBeOk();

        var observed = await DrainAsync(fake);
        observed.ShouldHaveSingleItem().StatusByte.ShouldBe<byte>(0x41);
        fake.LastStatusByteFor(DevName()).ShouldBe<byte>(0x44);
    }

    [Fact]
    public async Task Requests_raised_with_nobody_reading_are_capped_keeping_the_newest()
    {
        var fake = new FakeBackend().ActivateScenario(
            Scenario(new MockRule("*OPC", new RuleAction.Ack(), Srq: 0x60)),
            DevName()
        );
        var overflow = 3;

        for (var i = 0; i < ServiceRequestBuffer.Capacity + overflow; i++)
        {
            fake.RaiseServiceRequest(DevName(), (byte)(i % 256));
        }

        var observed = await DrainAsync(fake);
        observed.Count.ShouldBe(ServiceRequestBuffer.Capacity);
        observed[0].StatusByte.ShouldBe((byte)overflow);
    }
}
