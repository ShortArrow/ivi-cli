using System.Collections.Immutable;
using IviCli.Domain.Mock;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Domain.Tests.Mock;

/// <summary>
/// Behaviour tests for the scenario quirk profile of issue #115: a
/// scenario carries no quirks unless one is named, and the quirks a
/// scenario carries take part in its identity.
/// </summary>
public sealed class MockQuirksTests
{
    private static ScenarioName Name() => ScenarioName.From("wedge").ShouldBeOk();

    [Fact]
    public void Scenario_carries_no_quirks_by_default()
    {
        MockScenario.Empty(Name()).Quirks.ShouldBeNull();
        MockScenario
            .SingleScene(Name(), idnDefault: null, ImmutableArray<MockRule>.Empty)
            .Quirks.ShouldBeNull();
    }

    [Fact]
    public void Quirks_with_nothing_named_is_empty()
    {
        new MockQuirks().IsEmpty.ShouldBeTrue();
        new MockQuirks(SrqNotifyWedgeAfter: 0).IsEmpty.ShouldBeFalse();
        new MockQuirks(SrqNotifyWedgeAfter: 1).IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void Scenario_equality_accounts_for_quirks()
    {
        var plain = MockScenario.Empty(Name());
        var wedged = plain with { Quirks = new MockQuirks(SrqNotifyWedgeAfter: 1) };

        wedged.ShouldNotBe(plain);
        wedged.ShouldBe(plain with { Quirks = new MockQuirks(SrqNotifyWedgeAfter: 1) });
    }
}
