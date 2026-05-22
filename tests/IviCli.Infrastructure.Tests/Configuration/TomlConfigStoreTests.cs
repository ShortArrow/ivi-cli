using System.IO.Abstractions.TestingHelpers;
using IviCli.Application.Configuration;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.Infrastructure.Configuration;
using IviCli.TestKit;

namespace IviCli.Infrastructure.Tests.Configuration;

public class TomlConfigStoreTests
{
    private const string ConfigPath = "/etc/ivi-cli/config.toml";

    [Fact]
    public async Task LoadAsync_WhenFileMissing_ReturnsEmptyConfig()
    {
        // Given
        var fs = new MockFileSystem();
        var store = new TomlConfigStore(fs, ConfigPath);

        // When
        var result = await store.LoadAsync(CancellationToken.None);

        // Then
        result.ShouldBeOk().ShouldBe(ConfigDocument.Empty);
    }

    [Fact]
    public async Task LoadAsync_WithValidFile_ReturnsParsedConfig()
    {
        // Given
        var toml = """
            [defaults]
            device = "psu1"

            [[devices]]
            name = "psu1"
            resource = "TCPIP0::host::inst0::INSTR"
            timeout_ms = 3000
            """;
        var fs = new MockFileSystem(
            new Dictionary<string, MockFileData> { [ConfigPath] = new(toml) }
        );
        var store = new TomlConfigStore(fs, ConfigPath);

        // When
        var result = await store.LoadAsync(CancellationToken.None);

        // Then
        var config = result.ShouldBeOk();
        config.Devices.Length.ShouldBe(1);
        config.Defaults.Device!.Value.ShouldBe("psu1");
    }

    [Fact]
    public async Task LoadAsync_WithMalformedFile_ReturnsParseFailure()
    {
        // Given
        var fs = new MockFileSystem(
            new Dictionary<string, MockFileData> { [ConfigPath] = new("this is not toml [[[") }
        );
        var store = new TomlConfigStore(fs, ConfigPath);

        // When
        var result = await store.LoadAsync(CancellationToken.None);

        // Then
        result.ShouldBeError().ShouldBeOfType<ConfigStoreParseFailure>();
    }

    [Fact]
    public async Task SaveAsync_WritesFileAndCreatesDirectory()
    {
        // Given
        var fs = new MockFileSystem();
        var store = new TomlConfigStore(fs, ConfigPath);
        var doc = ConfigDocument
            .Empty.AddDevice(
                new Device(
                    DeviceName.From("psu1").ShouldBeOk(),
                    VisaResource.Parse("TCPIP0::host::inst0::INSTR").ShouldBeOk(),
                    Timeout.FromMilliseconds(3000).ShouldBeOk()
                )
            )
            .ShouldBeOk();

        // When
        var save = await store.SaveAsync(doc, CancellationToken.None);

        // Then
        save.ShouldBeOk();
        fs.File.Exists(ConfigPath).ShouldBeTrue();
        var written = fs.File.ReadAllText(ConfigPath);
        written.ShouldContain("psu1");
        written.ShouldContain("TCPIP0::host::inst0::INSTR");
    }

    [Fact]
    public async Task LoadAsync_AfterSaveAsync_RoundtripsTheDocument()
    {
        // Given
        var fs = new MockFileSystem();
        var store = new TomlConfigStore(fs, ConfigPath);
        var original = ConfigDocument
            .Empty.AddDevice(
                new Device(
                    DeviceName.From("psu1").ShouldBeOk(),
                    VisaResource.Parse("TCPIP0::host::inst0::INSTR").ShouldBeOk(),
                    Timeout.FromMilliseconds(3000).ShouldBeOk()
                )
            )
            .ShouldBeOk()
            .SetDefaultDevice(DeviceName.From("psu1").ShouldBeOk())
            .ShouldBeOk();

        // When
        (await store.SaveAsync(original, CancellationToken.None)).ShouldBeOk();
        var loaded = (await store.LoadAsync(CancellationToken.None)).ShouldBeOk();

        // Then
        loaded.ShouldBe(original);
    }

    [Fact]
    public async Task LoadAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        // Given
        var fs = new MockFileSystem();
        var store = new TomlConfigStore(fs, ConfigPath);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // When / Then
        await Should.ThrowAsync<OperationCanceledException>(() => store.LoadAsync(cts.Token));
    }
}
