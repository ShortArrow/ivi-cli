using IviCli.Application.Mock;
using IviCli.Domain;
using IviCli.Domain.Mock;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Application.Tests.Mock;

/// <summary>
/// Locks in the v0.2.2 `--initial &lt;scene&gt;` flag on
/// <c>mock scenario create</c>: a scenario created with an explicit
/// initial scene name starts with that scene as its only (empty)
/// scene, and <see cref="MockScenario.InitialScene"/> points at it.
/// Without the flag the v0.1.x-compatible <c>default</c> scene shape
/// is preserved.
/// </summary>
public sealed class CreateScenarioCommandHandlerTests
{
    [Fact]
    public async Task Create_without_initial_uses_synthetic_default_scene()
    {
        var store = new FakeScenarioStore();
        var handler = new CreateScenarioCommandHandler(store);

        var result = await handler.HandleAsync(new CreateScenarioCommand("demo"), default);
        result.ShouldBeOk();

        var loaded = (
            await store.LoadAsync(ScenarioName.From("demo").ShouldBeOk(), default)
        ).ShouldBeOk();
        loaded.InitialScene.ShouldBe(SceneName.DefaultScene());
        loaded.Scenes.Length.ShouldBe(1);
        loaded.Scenes[0].Name.ShouldBe(SceneName.DefaultScene());
    }

    [Fact]
    public async Task Create_with_initial_uses_named_scene_as_only_scene()
    {
        var store = new FakeScenarioStore();
        var handler = new CreateScenarioCommandHandler(store);

        var result = await handler.HandleAsync(
            new CreateScenarioCommand("psu-fsm", InitialScene: "off"),
            default
        );
        result.ShouldBeOk();

        var loaded = (
            await store.LoadAsync(ScenarioName.From("psu-fsm").ShouldBeOk(), default)
        ).ShouldBeOk();
        var off = SceneName.From("off").ShouldBeOk();
        loaded.InitialScene.ShouldBe(off);
        loaded.Scenes.Length.ShouldBe(1);
        loaded.Scenes[0].Name.ShouldBe(off);
        loaded.Scenes[0].Rules.Length.ShouldBe(0);
    }

    [Fact]
    public async Task Create_with_invalid_initial_scene_name_fails_specifically()
    {
        var store = new FakeScenarioStore();
        var handler = new CreateScenarioCommandHandler(store);

        var result = await handler.HandleAsync(
            new CreateScenarioCommand("demo", InitialScene: "INVALID NAME"),
            default
        );
        result.ShouldBeError().ShouldBeOfType<CreateScenarioInvalidInitialScene>();
    }

    [Fact]
    public async Task Create_with_initial_and_idn_preserves_both()
    {
        var store = new FakeScenarioStore();
        var handler = new CreateScenarioCommandHandler(store);

        var result = await handler.HandleAsync(
            new CreateScenarioCommand("demo", IdnDefault: "ACME,X,1,1.0", InitialScene: "ready"),
            default
        );
        result.ShouldBeOk();

        var loaded = (
            await store.LoadAsync(ScenarioName.From("demo").ShouldBeOk(), default)
        ).ShouldBeOk();
        loaded.IdnDefault.ShouldBe("ACME,X,1,1.0");
        loaded.InitialScene.ShouldBe(SceneName.From("ready").ShouldBeOk());
    }
}
