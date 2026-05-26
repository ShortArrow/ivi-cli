using IviCli.Cli.Completion.Completers;
using IviCli.Domain.Mock;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Cli.Tests.Completion.Completers;

public sealed class ScenarioNameCompleterTests
{
    private static MockScenario Sc(string name) =>
        MockScenario.Empty(ScenarioName.From(name).ShouldBeOk());

    [Fact]
    public async Task CompleteAsync_returns_all_scenarios_when_prefix_empty()
    {
        MockScenario[] seed = [Sc("alpha"), Sc("beta")];
        var store = new FakeScenarioStore(seed);
        var completer = new ScenarioNameCompleter(store);
        var candidates = await completer.CompleteAsync(string.Empty, default);
        string[] expected = ["alpha", "beta"];
        candidates.ShouldBe(expected);
    }

    [Fact]
    public async Task CompleteAsync_filters_by_prefix()
    {
        MockScenario[] seed = [Sc("alpha"), Sc("beta"), Sc("alphabet")];
        var store = new FakeScenarioStore(seed);
        var completer = new ScenarioNameCompleter(store);
        var candidates = await completer.CompleteAsync("al", default);
        string[] expected = ["alpha", "alphabet"];
        candidates.ShouldBe(expected);
    }
}
