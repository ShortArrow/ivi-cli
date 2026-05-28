using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using IviCli.Domain.Configuration;
using IviCli.Infrastructure.Plugins;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Infrastructure.Tests.Plugins;

public sealed class PluginLoaderTests
{
    [Fact]
    public void LoadAll_returns_empty_when_plugins_disabled()
    {
        var fs = new MockFileSystem();
        var loader = new PluginLoader(fs);
        var result = loader.LoadAll(PluginsConfig.Default, "/plugins");
        result.ShouldBeEmpty();
    }

    [Fact]
    public void LoadAll_returns_empty_when_directory_missing()
    {
        var fs = new MockFileSystem();
        var loader = new PluginLoader(fs);
        var enabled = PluginsConfig.From(true, ImmutableArray<string>.Empty).ShouldBeOk();
        var result = loader.LoadAll(enabled, "/does-not-exist");
        result.ShouldBeEmpty();
    }

    [Fact]
    public void LoadOne_missing_manifest_returns_PluginManifestMissing()
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory("/plugins/acme");
        var loader = new PluginLoader(fs);
        var enabled = PluginsConfig.From(true, ImmutableArray<string>.Empty).ShouldBeOk();

        var result = loader.LoadOne(enabled, "/plugins/acme");

        result.ShouldBeError().ShouldBeOfType<PluginManifestMissing>();
    }

    [Fact]
    public void LoadOne_malformed_manifest_returns_PluginManifestSyntaxError()
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory("/plugins/acme");
        fs.AddFile("/plugins/acme/plugin.toml", new MockFileData("this is not toml"));
        var loader = new PluginLoader(fs);
        var enabled = PluginsConfig.From(true, ImmutableArray<string>.Empty).ShouldBeOk();

        var result = loader.LoadOne(enabled, "/plugins/acme");

        result.ShouldBeError().ShouldBeOfType<PluginManifestSyntaxError>();
    }

    [Fact]
    public void LoadOne_wrong_api_version_returns_PluginApiVersionMismatch()
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory("/plugins/acme");
        fs.AddFile(
            "/plugins/acme/plugin.toml",
            new MockFileData(
                """
                [plugin]
                name = "acme"
                version = "1.0.0"
                target_api_version = 99
                entry_point = "Acme.Plugin"
                assembly = "Acme.dll"
                """
            )
        );
        var loader = new PluginLoader(fs);
        var enabled = PluginsConfig.From(true, ImmutableArray<string>.Empty).ShouldBeOk();

        var result = loader.LoadOne(enabled, "/plugins/acme");

        var err = result.ShouldBeError().ShouldBeOfType<PluginApiVersionMismatch>();
        err.Plugin.ShouldBe(99);
        err.Host.ShouldBe(IviCli.Plugin.HostApiVersion.Current);
    }

    [Fact]
    public void LoadOne_not_in_allowlist_returns_PluginNotAllowed()
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory("/plugins/rogue");
        fs.AddFile(
            "/plugins/rogue/plugin.toml",
            new MockFileData(
                """
                [plugin]
                name = "rogue"
                version = "1.0.0"
                target_api_version = 1
                entry_point = "Rogue.Plugin"
                assembly = "Rogue.dll"
                """
            )
        );
        var loader = new PluginLoader(fs);
        var enabled = PluginsConfig.From(true, ImmutableArray.Create("acme")).ShouldBeOk();

        var result = loader.LoadOne(enabled, "/plugins/rogue");

        result.ShouldBeError().ShouldBeOfType<PluginNotAllowed>();
    }

    [Fact]
    public void LoadOne_assembly_missing_returns_PluginAssemblyMissing()
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory("/plugins/acme");
        fs.AddFile(
            "/plugins/acme/plugin.toml",
            new MockFileData(
                """
                [plugin]
                name = "acme"
                version = "1.0.0"
                target_api_version = 1
                entry_point = "Acme.Plugin"
                assembly = "Acme.dll"
                """
            )
        );
        var loader = new PluginLoader(fs);
        var enabled = PluginsConfig.From(true, ImmutableArray<string>.Empty).ShouldBeOk();

        var result = loader.LoadOne(enabled, "/plugins/acme");

        result.ShouldBeError().ShouldBeOfType<PluginAssemblyMissing>();
    }
}
