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

    /// <summary>
    /// A terminator on a transport that ends messages itself has no effect,
    /// so loading a device that sets one warns (ADR 0055 §5).
    /// </summary>
    [Fact]
    public async Task LoadAsync_DeviceWithAnIgnoredTerminator_Warns()
    {
        // Given
        var toml = """
            [[devices]]
            name = "scope"
            resource = "TCPIP0::host::hislip0::INSTR"
            timeout_ms = 3000
            read_termination = "\r"

            [[devices]]
            name = "psu"
            resource = "TCPIP0::host::5025::SOCKET"
            timeout_ms = 3000
            read_termination = "\r"
            """;
        var fs = new MockFileSystem(
            new Dictionary<string, MockFileData> { [ConfigPath] = new MockFileData(toml) }
        );
        var logger = new RecordingLogger<TomlConfigStore>();
        var store = new TomlConfigStore(fs, ConfigPath, logger);

        // When
        var result = await store.LoadAsync(CancellationToken.None);

        // Then
        result.ShouldBeOk();
        var warning = logger.Entries.ShouldHaveSingleItem();
        warning.Level.ShouldBe(Microsoft.Extensions.Logging.LogLevel.Warning);
        warning.Message.ShouldContain("scope");
    }

    /// <summary>
    /// A long-running server loads the configuration for every request, so
    /// a device is warned about once per store.
    /// </summary>
    [Fact]
    public async Task LoadAsync_Again_DoesNotRepeatTheWarning()
    {
        // Given
        var toml = """
            [[devices]]
            name = "scope"
            resource = "TCPIP0::host::hislip0::INSTR"
            timeout_ms = 3000
            read_termination = "\r"
            """;
        var fs = new MockFileSystem(
            new Dictionary<string, MockFileData> { [ConfigPath] = new MockFileData(toml) }
        );
        var logger = new RecordingLogger<TomlConfigStore>();
        var store = new TomlConfigStore(fs, ConfigPath, logger);

        // When
        (await store.LoadAsync(CancellationToken.None)).ShouldBeOk();
        (await store.LoadAsync(CancellationToken.None)).ShouldBeOk();

        // Then
        logger.Entries.ShouldHaveSingleItem();
    }
}
