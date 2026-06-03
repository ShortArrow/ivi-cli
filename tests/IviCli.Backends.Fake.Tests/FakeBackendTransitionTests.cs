using System.Collections.Immutable;
using IviCli.Backends.Fake;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Backends.Fake.Tests;

/// <summary>
/// Locks in the state-machine semantics introduced in B0.2-3 of
/// issue #26: a matched rule whose action carries a
/// <c>Transition</c> moves the FakeBackend to a different scene,
/// so the same SCPI command can produce different responses across
/// the session.
/// </summary>
public sealed class FakeBackendTransitionTests
{
    private static readonly SceneName Off = SceneName.From("off").ShouldBeOk();
    private static readonly SceneName On = SceneName.From("on").ShouldBeOk();

    private static Device Dev() =>
        new(
            DeviceName.From("psu1").ShouldBeOk(),
            VisaResource.Parse("TCPIP0::host::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    /// <summary>Builds a 2-state PSU scenario:
    /// off: OUTP? -> 0; OUTP ON -> ack + transition to on
    /// on:  OUTP? -> 1; OUTP OFF -> ack + transition to off
    /// initial scene: off.</summary>
    private static MockScenario PsuStateMachine()
    {
        var offScene = new MockScene(
            Off,
            ImmutableArray.Create(
                new MockRule("OUTP?", new RuleAction.Respond("0")),
                new MockRule("OUTP ON", new RuleAction.Ack(Transition: On))
            )
        );
        var onScene = new MockScene(
            On,
            ImmutableArray.Create(
                new MockRule("OUTP?", new RuleAction.Respond("1")),
                new MockRule("OUTP OFF", new RuleAction.Ack(Transition: Off))
            )
        );
        return new MockScenario(
            ScenarioName.From("psu-fsm").ShouldBeOk(),
            InitialScene: Off,
            IdnDefault: null,
            Scenes: ImmutableArray.Create(offScene, onScene)
        );
    }

    [Fact]
    public async Task State_machine_walks_off_on_off_via_OUTP_writes()
    {
        var backend = new FakeBackend().ActivateScenario(PsuStateMachine());

        // Initially in `off`: OUTP? -> 0
        (
            await backend.QueryAsync(
                Dev(),
                ScpiQuery.From("OUTP?").ShouldBeOk(),
                CancellationToken.None
            )
        )
            .ShouldBeOk()
            .ShouldBe("0");

        backend.CurrentScene.ShouldBe(Off);

        // OUTP ON acks + transitions to `on`
        (
            await backend.WriteAsync(
                Dev(),
                ScpiCommand.From("OUTP ON").ShouldBeOk(),
                CancellationToken.None
            )
        ).ShouldBeOk();
        backend.CurrentScene.ShouldBe(On);

        // Now in `on`: OUTP? -> 1
        (
            await backend.QueryAsync(
                Dev(),
                ScpiQuery.From("OUTP?").ShouldBeOk(),
                CancellationToken.None
            )
        )
            .ShouldBeOk()
            .ShouldBe("1");

        // OUTP OFF acks + transitions back to `off`
        (
            await backend.WriteAsync(
                Dev(),
                ScpiCommand.From("OUTP OFF").ShouldBeOk(),
                CancellationToken.None
            )
        ).ShouldBeOk();
        backend.CurrentScene.ShouldBe(Off);

        // Back to 0
        (
            await backend.QueryAsync(
                Dev(),
                ScpiQuery.From("OUTP?").ShouldBeOk(),
                CancellationToken.None
            )
        )
            .ShouldBeOk()
            .ShouldBe("0");
    }

    [Fact]
    public async Task Rule_in_other_scene_does_not_match_while_current_scene_is_off()
    {
        // In `off`, OUTP OFF has no rule (only in `on`). The backend
        // should fall through to its default Write success (the
        // rule isn't applied; no transition happens).
        var backend = new FakeBackend().ActivateScenario(PsuStateMachine());

        (
            await backend.WriteAsync(
                Dev(),
                ScpiCommand.From("OUTP OFF").ShouldBeOk(),
                CancellationToken.None
            )
        ).ShouldBeOk();

        backend.CurrentScene.ShouldBe(Off, "no matching rule should not move the scene");
    }

    [Fact]
    public async Task Respond_with_transition_returns_text_and_swaps_scene()
    {
        // A Respond rule with a Transition: client sees the response,
        // scene moves on for next time. Useful for a single-shot
        // *ARM? that primes the next stage.
        var armed = SceneName.From("armed").ShouldBeOk();
        var idleScene = new MockScene(
            Off,
            ImmutableArray.Create(
                new MockRule("ARM?", new RuleAction.Respond("ARMED", Transition: armed))
            )
        );
        var armedScene = new MockScene(
            armed,
            ImmutableArray.Create(new MockRule("STAT?", new RuleAction.Respond("READY")))
        );
        var scenario = new MockScenario(
            ScenarioName.From("two-stage").ShouldBeOk(),
            InitialScene: Off,
            IdnDefault: null,
            Scenes: ImmutableArray.Create(idleScene, armedScene)
        );

        var backend = new FakeBackend().ActivateScenario(scenario);

        (
            await backend.QueryAsync(
                Dev(),
                ScpiQuery.From("ARM?").ShouldBeOk(),
                CancellationToken.None
            )
        )
            .ShouldBeOk()
            .ShouldBe("ARMED");
        backend.CurrentScene.ShouldBe(armed);

        // STAT? is only in the armed scene; reachable now.
        (
            await backend.QueryAsync(
                Dev(),
                ScpiQuery.From("STAT?").ShouldBeOk(),
                CancellationToken.None
            )
        )
            .ShouldBeOk()
            .ShouldBe("READY");
    }

    [Fact]
    public async Task Transition_to_nonexistent_scene_is_silently_ignored()
    {
        // Authoring mistake: rule transitions to a scene name that
        // is not defined in the scenario. The rule's effect still
        // applies (so existing test scripts don't break), but the
        // current scene does not move. A future B0.2 patch may
        // surface this as MockScenarioContractMismatch.
        var ghost = SceneName.From("ghost").ShouldBeOk();
        var scene = new MockScene(
            Off,
            ImmutableArray.Create(new MockRule("OUTP ON", new RuleAction.Ack(Transition: ghost)))
        );
        var scenario = new MockScenario(
            ScenarioName.From("bad-fsm").ShouldBeOk(),
            InitialScene: Off,
            IdnDefault: null,
            Scenes: ImmutableArray.Create(scene)
        );

        var backend = new FakeBackend().ActivateScenario(scenario);
        (
            await backend.WriteAsync(
                Dev(),
                ScpiCommand.From("OUTP ON").ShouldBeOk(),
                CancellationToken.None
            )
        ).ShouldBeOk();

        backend.CurrentScene.ShouldBe(Off, "transition target must exist to be honoured");
    }

    [Fact]
    public void Reactivating_scenario_resets_current_scene_to_initial()
    {
        var backend = new FakeBackend();
        backend.ActivateScenario(PsuStateMachine());
        backend.CurrentScene.ShouldBe(Off);

        // Re-activating a (different or same) scenario must reset.
        backend.DeactivateScenario();
        backend.CurrentScene.ShouldBeNull();

        backend.ActivateScenario(PsuStateMachine());
        backend.CurrentScene.ShouldBe(Off);
    }
}
