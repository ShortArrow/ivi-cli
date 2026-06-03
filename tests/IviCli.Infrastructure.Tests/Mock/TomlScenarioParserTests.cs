using System.Collections.Immutable;
using IviCli.Domain;
using IviCli.Domain.Mock;
using IviCli.Infrastructure.Mock;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Infrastructure.Tests.Mock;

/// <summary>
/// Round-trip and schema-detection tests for the v0.2.0 TOML scenario
/// parser/serialiser (issue #26 B0.2-2).
/// </summary>
public sealed class TomlScenarioParserTests
{
    [Fact]
    public void V0_1_flat_TOML_loads_as_single_default_scene()
    {
        const string toml = """
            idn = "ACME,X,1,1.0"

            [[scenes]]
            match = "*IDN?"
            respond = "ACME,X,1,1.0"

            [[scenes]]
            match = "OUTP ON"
            ack = true
            """;
        var scenario = TomlScenarioParser
            .Parse(ScenarioName.From("demo").ShouldBeOk(), toml)
            .ShouldBeOk();

        scenario.Scenes.Length.ShouldBe(1);
        scenario.Scenes[0].Name.ShouldBe(SceneName.DefaultScene());
        scenario.InitialScene.ShouldBe(SceneName.DefaultScene());
        scenario.Scenes[0].Rules.Length.ShouldBe(2);
        scenario.IdnDefault.ShouldBe("ACME,X,1,1.0");
    }

    [Fact]
    public void V0_2_multi_scene_TOML_round_trips_with_transitions_preserved()
    {
        var off = SceneName.From("off").ShouldBeOk();
        var on = SceneName.From("on").ShouldBeOk();

        var scenario = new MockScenario(
            ScenarioName.From("psu-fsm").ShouldBeOk(),
            InitialScene: off,
            IdnDefault: "IVICLI-MOCK,PSU,SN0001,1.0.0",
            Scenes: ImmutableArray.Create(
                new MockScene(
                    off,
                    ImmutableArray.Create(
                        new MockRule("OUTP?", new RuleAction.Respond("0")),
                        new MockRule("OUTP ON", new RuleAction.Ack(Transition: on))
                    )
                ),
                new MockScene(
                    on,
                    ImmutableArray.Create(
                        new MockRule("OUTP?", new RuleAction.Respond("1")),
                        new MockRule("OUTP OFF", new RuleAction.Ack(Transition: off))
                    )
                )
            )
        );

        var serialized = TomlScenarioParser.Serialize(scenario);
        serialized.ShouldContain("initial_scene = \"off\"");
        serialized.ShouldContain("[[scenes.rules]]");
        serialized.ShouldContain("transition_to = \"on\"");
        serialized.ShouldContain("transition_to = \"off\"");

        var loaded = TomlScenarioParser
            .Parse(ScenarioName.From("psu-fsm").ShouldBeOk(), serialized)
            .ShouldBeOk();

        loaded.InitialScene.ShouldBe(off);
        loaded.Scenes.Length.ShouldBe(2);
        loaded.Scenes[0].Name.ShouldBe(off);
        loaded.Scenes[0].Rules.Length.ShouldBe(2);
        loaded.Scenes[0].Rules[1].Action.Transition.ShouldBe(on);
        loaded.Scenes[1].Name.ShouldBe(on);
        loaded.Scenes[1].Rules[1].Action.Transition.ShouldBe(off);
    }

    [Fact]
    public void Single_default_scene_no_transitions_round_trips_as_flat_v0_1_shape()
    {
        // No FSM, no extra scenes — emit the v0.1.x flat shape so
        // pre-v0.2 scenario files survive a load-save cycle without
        // gaining noisy `initial_scene` / `[[scenes.rules]]` keys.
        var scenario = MockScenario.SingleScene(
            ScenarioName.From("demo").ShouldBeOk(),
            idnDefault: "ACME,X,1,1.0",
            rules: ImmutableArray.Create(
                new MockRule("*IDN?", new RuleAction.Respond("ACME,X,1,1.0")),
                new MockRule("OUTP ON", new RuleAction.Ack())
            )
        );
        var serialized = TomlScenarioParser.Serialize(scenario);

        serialized.ShouldNotContain("initial_scene");
        serialized.ShouldNotContain("[[scenes.rules]]");
        serialized.ShouldNotContain("name =");
        serialized.ShouldContain("[[scenes]]");
        serialized.ShouldContain("respond = \"ACME,X,1,1.0\"");
    }

    [Fact]
    public void Empty_scenario_load_round_trips()
    {
        var scenario = MockScenario.Empty(ScenarioName.From("empty").ShouldBeOk());
        var serialized = TomlScenarioParser.Serialize(scenario);
        var loaded = TomlScenarioParser
            .Parse(ScenarioName.From("empty").ShouldBeOk(), serialized)
            .ShouldBeOk();
        loaded.Scenes.Length.ShouldBe(1);
        loaded.Scenes[0].Rules.Length.ShouldBe(0);
        loaded.InitialScene.ShouldBe(SceneName.DefaultScene());
    }

    [Fact]
    public void Invalid_initial_scene_reference_surfaces_a_parse_failure()
    {
        const string toml = """
            initial_scene = "ghost"

            [[scenes]]
            name = "off"
            """;
        var err = TomlScenarioParser
            .Parse(ScenarioName.From("demo").ShouldBeOk(), toml)
            .ShouldBeError();
        err.ShouldBeOfType<IviCli.Application.Mock.ScenarioStoreParseFailure>();
    }
}
