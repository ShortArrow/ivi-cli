using System.Collections.Immutable;
using IviCli.Domain.Configuration;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Domain.Tests.Configuration;

public sealed class PluginsConfigTests
{
    [Fact]
    public void Default_is_disabled_with_empty_allowlist()
    {
        PluginsConfig.Default.Enabled.ShouldBeFalse();
        PluginsConfig.Default.Allowed.IsDefaultOrEmpty.ShouldBeTrue();
    }

    [Fact]
    public void From_blank_allowed_entry_fails()
    {
        var result = PluginsConfig.From(true, ImmutableArray.Create("ok", "  "));
        result.ShouldBeError().ShouldBeOfType<PluginsAllowedEntryBlank>();
    }

    [Fact]
    public void IsAllowed_empty_list_allows_every_name()
    {
        PluginsConfig.Default.IsAllowed("anything").ShouldBeTrue();
    }

    [Fact]
    public void IsAllowed_with_explicit_list_only_matches_listed_names()
    {
        var cfg = PluginsConfig.From(true, ImmutableArray.Create("acme", "vendor-x")).ShouldBeOk();
        cfg.IsAllowed("acme").ShouldBeTrue();
        cfg.IsAllowed("vendor-x").ShouldBeTrue();
        cfg.IsAllowed("rogue").ShouldBeFalse();
    }

    [Fact]
    public void Equality_is_structural()
    {
        var a = PluginsConfig.From(true, ImmutableArray.Create("acme")).ShouldBeOk();
        var b = PluginsConfig.From(true, ImmutableArray.Create("acme")).ShouldBeOk();
        var c = PluginsConfig.From(true, ImmutableArray.Create("other")).ShouldBeOk();
        a.ShouldBe(b);
        a.ShouldNotBe(c);
    }
}
