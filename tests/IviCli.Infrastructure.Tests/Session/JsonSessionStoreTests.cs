using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using IviCli.Application.Session;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;
using IviCli.Domain.Session;
using IviCli.Infrastructure.Session;
using IviCli.TestKit;

namespace IviCli.Infrastructure.Tests.Session;

public class JsonSessionStoreTests
{
    private const string Path = "/var/lib/ivi-cli/session.json";

    [Fact]
    public async Task LoadAsync_WhenFileMissing_ReturnsEmpty()
    {
        // Given
        var fs = new MockFileSystem();
        var store = new JsonSessionStore(fs, Path);

        // When
        var result = await store.LoadAsync(CancellationToken.None);

        // Then
        result.ShouldBeOk().ShouldBe(SessionState.Empty);
    }

    [Fact]
    public async Task SaveThenLoad_RoundtripsCurrentDevice()
    {
        // Given
        var fs = new MockFileSystem();
        var store = new JsonSessionStore(fs, Path);
        var state = new SessionState(
            DeviceName.From("psu1").ShouldBeOk(),
            ImmutableDictionary<DeviceName, ScenarioName>.Empty
        );

        // When
        (await store.SaveAsync(state, CancellationToken.None)).ShouldBeOk();
        var loaded = (await store.LoadAsync(CancellationToken.None)).ShouldBeOk();

        // Then
        loaded.CurrentDevice.ShouldNotBeNull();
        loaded.CurrentDevice!.Value.ShouldBe("psu1");
    }

    [Fact]
    public async Task SaveThenLoad_RoundtripsNullCurrentDevice()
    {
        // Given
        var fs = new MockFileSystem();
        var store = new JsonSessionStore(fs, Path);

        // When
        (await store.SaveAsync(SessionState.Empty, CancellationToken.None)).ShouldBeOk();
        var loaded = (await store.LoadAsync(CancellationToken.None)).ShouldBeOk();

        // Then
        loaded.CurrentDevice.ShouldBeNull();
    }

    [Fact]
    public async Task ConcurrentSaves_NeverLoseTheSessionFile()
    {
        // Given: two writers persisting the same binding to one session
        // path, as the mock container's two gateway processes do at startup.
        // A reader that finds the file missing treats it as an empty session,
        // which tears down every live scenario binding — so no interleaving
        // of saves may leave the file absent or a save reporting failure.
        var fs = new System.IO.Abstractions.FileSystem();
        var dir = fs.Path.Combine(
            fs.Path.GetTempPath(),
            "ivi-session-race-" + Guid.NewGuid().ToString("N")
        );
        fs.Directory.CreateDirectory(dir);
        try
        {
            var path = fs.Path.Combine(dir, "session.json");
            var state = new SessionState(
                null,
                ImmutableDictionary<DeviceName, ScenarioName>.Empty.Add(
                    DeviceName.From("mock1").ShouldBeOk(),
                    ScenarioName.From("default").ShouldBeOk()
                )
            );
            var writerA = new JsonSessionStore(fs, path);
            var writerB = new JsonSessionStore(fs, path);

            for (var round = 0; round < 200; round++)
            {
                // When
                var results = await Task.WhenAll(
                    Task.Run(() => writerA.SaveAsync(state, CancellationToken.None)),
                    Task.Run(() => writerB.SaveAsync(state, CancellationToken.None))
                );

                // Then
                results[0].ShouldBeOk();
                results[1].ShouldBeOk();
                var loaded = (await writerA.LoadAsync(CancellationToken.None)).ShouldBeOk();
                loaded.DeviceScenarios.ShouldNotBeEmpty();
            }
        }
        finally
        {
            fs.Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_WithInvalidJson_ReturnsParseFailure()
    {
        // Given
        var fs = new MockFileSystem(
            new Dictionary<string, MockFileData> { [Path] = new("{not json") }
        );
        var store = new JsonSessionStore(fs, Path);

        // When
        var result = await store.LoadAsync(CancellationToken.None);

        // Then
        result.ShouldBeError().ShouldBeOfType<SessionStoreParseFailure>();
    }

    [Fact]
    public async Task LoadAsync_WithInvalidDeviceName_ReturnsParseFailure()
    {
        // Given
        var fs = new MockFileSystem(
            new Dictionary<string, MockFileData>
            {
                [Path] = new("{\"current_device\":\"Invalid Name!\"}"),
            }
        );
        var store = new JsonSessionStore(fs, Path);

        // When
        var result = await store.LoadAsync(CancellationToken.None);

        // Then
        result.ShouldBeError().ShouldBeOfType<SessionStoreParseFailure>();
    }

    [Fact]
    public async Task LoadAsync_WhenCancelled_Throws()
    {
        // Given
        var fs = new MockFileSystem();
        var store = new JsonSessionStore(fs, Path);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // When / Then
        await Should.ThrowAsync<OperationCanceledException>(() => store.LoadAsync(cts.Token));
    }
}
