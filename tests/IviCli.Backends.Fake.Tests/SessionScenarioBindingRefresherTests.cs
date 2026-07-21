using System.Collections.Immutable;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;
using IviCli.Domain.Scpi;
using IviCli.Domain.Session;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Microsoft.Extensions.Logging.Abstractions;

namespace IviCli.Backends.Fake.Tests;

/// <summary>
/// Locks in the live re-sync semantics of
/// <see cref="SessionScenarioBindingRefresher"/> (issue: a serving SOCKET
/// gateway must reflect a separate `mock scenario activate` without a
/// restart, but must not reset in-flight scene state for an unchanged
/// binding).
/// </summary>
public sealed class SessionScenarioBindingRefresherTests
{
    private static readonly SceneName Off = SceneName.From("off").ShouldBeOk();
    private static readonly SceneName On = SceneName.From("on").ShouldBeOk();

    private static DeviceName DevName() => DeviceName.From("psu1").ShouldBeOk();

    private static Device Dev() =>
        new(
            DevName(),
            VisaResource.Parse("TCPIP0::host::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    private static ScenarioName ScnName(string value) => ScenarioName.From(value).ShouldBeOk();

    private static MockScenario SingleRuleScenario(string name, string match, string response) =>
        MockScenario.SingleScene(
            ScnName(name),
            idnDefault: null,
            rules: ImmutableArray.Create(new MockRule(match, new RuleAction.Respond(response)))
        );

    private static MockScenario PsuStateMachine(string name)
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
            ScnName(name),
            InitialScene: Off,
            IdnDefault: null,
            Scenes: ImmutableArray.Create(offScene, onScene)
        );
    }

    private static SessionState SessionWith(string scenarioName) =>
        SessionState.Empty.BindScenario(DevName(), ScnName(scenarioName));

    private static SessionScenarioBindingRefresher Refresher(
        FakeBackend backend,
        FakeScenarioStore scenarios,
        FakeSessionStore sessions
    ) => new(backend, scenarios, sessions, NullLogger<SessionScenarioBindingRefresher>.Instance);

    [Fact]
    public async Task ChangedBinding_ReActivatesToNewScenario()
    {
        // Given a backend bound to scenario "a" but a session now binding "b".
        var scenarioA = SingleRuleScenario("a", "MEAS:VOLT?", "1.00");
        var scenarioB = SingleRuleScenario("b", "MEAS:VOLT?", "2.00");
        var backend = new FakeBackend();
        backend.ActivateScenario(scenarioA, DevName());
        var refresher = Refresher(
            backend,
            new FakeScenarioStore(new[] { scenarioA, scenarioB }),
            new FakeSessionStore(SessionWith("b"))
        );

        // When
        await refresher.RefreshAsync(Dev(), CancellationToken.None);

        // Then the backend now serves scenario "b".
        backend.GetActiveScenario(DevName())!.Name.ShouldBe(ScnName("b"));
        var result = await backend.QueryAsync(
            Dev(),
            ScpiQuery.From("MEAS:VOLT?").ShouldBeOk(),
            CancellationToken.None
        );
        result.ShouldBeOk().ShouldBe("2.00");
    }

    [Fact]
    public async Task UnchangedBinding_PreservesSceneState()
    {
        // Given a backend bound to a state machine, transitioned to "on".
        var scenario = PsuStateMachine("psu");
        var backend = new FakeBackend();
        backend.ActivateScenario(scenario, DevName());
        await backend.WriteAsync(
            Dev(),
            ScpiCommand.From("OUTP ON").ShouldBeOk(),
            CancellationToken.None
        );
        backend.GetCurrentScene(DevName()).ShouldBe(On);

        var refresher = Refresher(
            backend,
            new FakeScenarioStore(new[] { scenario }),
            new FakeSessionStore(SessionWith("psu"))
        );

        // When the session still binds the same scenario.
        await refresher.RefreshAsync(Dev(), CancellationToken.None);

        // Then the current scene is NOT reset to the initial scene.
        backend.GetCurrentScene(DevName()).ShouldBe(On);
    }

    [Fact]
    public async Task NoBindingInSession_DeactivatesExistingBinding()
    {
        // Given a backend bound to a scenario but a session with no binding.
        var scenario = SingleRuleScenario("a", "*IDN?", "FROM,SCENARIO");
        var backend = new FakeBackend();
        backend.ActivateScenario(scenario, DevName());
        var refresher = Refresher(
            backend,
            new FakeScenarioStore(new[] { scenario }),
            new FakeSessionStore(SessionState.Empty)
        );

        // When
        await refresher.RefreshAsync(Dev(), CancellationToken.None);

        // Then the binding is removed.
        backend.GetActiveScenario(DevName()).ShouldBeNull();
        backend.HasActiveScenarioFor(Dev()).ShouldBeFalse();
    }
}
