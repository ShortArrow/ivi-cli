using IviCli.Plugin;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Plugin.Tests;

public sealed class PluginManifestTests
{
    [Fact]
    public void From_round_trips_when_all_fields_present()
    {
        var m = PluginManifest.From("acme", "1.0.0", 1, "Acme.Plugin", "Acme.dll").ShouldBeOk();
        m.Name.ShouldBe("acme");
        m.Version.ShouldBe("1.0.0");
        m.TargetApiVersion.ShouldBe(1);
        m.EntryPoint.ShouldBe("Acme.Plugin");
        m.Assembly.ShouldBe("Acme.dll");
    }

    [Theory]
    [InlineData("", "1", 1, "E", "A.dll", "name")]
    [InlineData("n", "", 1, "E", "A.dll", "version")]
    [InlineData("n", "1", 1, "", "A.dll", "entry_point")]
    [InlineData("n", "1", 1, "E", "", "assembly")]
    public void From_blank_required_field_returns_PluginManifestFieldBlank(
        string name,
        string version,
        int api,
        string entry,
        string assembly,
        string expectedField
    )
    {
        var result = PluginManifest.From(name, version, api, entry, assembly);
        var err = result.ShouldBeError().ShouldBeOfType<PluginManifestFieldBlank>();
        err.Field.ShouldBe(expectedField);
    }

    [Fact]
    public void From_non_positive_api_version_fails()
    {
        var result = PluginManifest.From("n", "1", 0, "E", "A.dll");
        result.ShouldBeError().ShouldBeOfType<PluginManifestInvalidApiVersion>();
    }

    [Fact]
    public void HostApiVersion_Current_is_1()
    {
        HostApiVersion.Current.ShouldBe(1);
    }
}
