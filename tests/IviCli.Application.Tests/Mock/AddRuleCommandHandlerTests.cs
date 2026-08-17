using IviCli.Application.Mock;
using IviCli.Domain;
using IviCli.Domain.Mock;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Application.Tests.Mock;

/// <summary>
/// The add-rule command carries the optional status byte a rule raises a
/// service request with, so an operator can build the IEEE 488.2
/// operation-complete pattern without hand-editing the scenario file.
/// </summary>
public sealed class AddRuleCommandHandlerTests
{
    [Fact]
    public async Task Srq_is_stored_on_the_added_rule()
    {
        var name = ScenarioName.From("demo").ShouldBeOk();
        var store = new FakeScenarioStore(new[] { MockScenario.Empty(name) });
        var handler = new AddRuleCommandHandler(store);

        var result = await handler.HandleAsync(
            new AddRuleCommand(
                "demo",
                Scene: null,
                Match: "*OPC",
                Respond: null,
                Ack: true,
                Fail: null,
                FailDetail: null,
                TransitionTo: null,
                Srq: 0x60
            ),
            default
        );
        result.ShouldBeOk();

        var loaded = (await store.LoadAsync(name, default)).ShouldBeOk();
        loaded.Scenes[0].Rules.ShouldHaveSingleItem().Srq.ShouldBe<byte?>(0x60);
    }

    [Fact]
    public async Task A_rule_added_without_srq_carries_none()
    {
        var name = ScenarioName.From("demo").ShouldBeOk();
        var store = new FakeScenarioStore(new[] { MockScenario.Empty(name) });
        var handler = new AddRuleCommandHandler(store);

        var result = await handler.HandleAsync(
            new AddRuleCommand(
                "demo",
                Scene: null,
                Match: "*OPC",
                Respond: null,
                Ack: true,
                Fail: null,
                FailDetail: null,
                TransitionTo: null
            ),
            default
        );
        result.ShouldBeOk();

        var loaded = (await store.LoadAsync(name, default)).ShouldBeOk();
        loaded.Scenes[0].Rules.ShouldHaveSingleItem().Srq.ShouldBeNull();
    }
}
