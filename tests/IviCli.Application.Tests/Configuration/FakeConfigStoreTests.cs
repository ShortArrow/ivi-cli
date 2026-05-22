using IviCli.Application.Configuration;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Timeout = IviCli.Domain.Timeout;

namespace IviCli.Application.Tests.Configuration;

public class FakeConfigStoreTests
{
    private static Device MakeDevice() =>
        new(
            DeviceName.From("psu1").ShouldBeOk(),
            VisaResource.Parse("TCPIP0::host::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    [Fact]
    public async Task LoadAsync_FromEmpty_ReturnsEmptyConfig()
    {
        // Given
        var store = new FakeConfigStore();

        // When
        var result = await store.LoadAsync(CancellationToken.None);

        // Then
        result.ShouldBeOk().ShouldBe(ConfigDocument.Empty);
    }

    [Fact]
    public async Task LoadAsync_WithInitialConfig_ReturnsThatConfig()
    {
        // Given
        var initial = ConfigDocument.Empty.AddDevice(MakeDevice()).ShouldBeOk();
        var store = new FakeConfigStore(initial);

        // When
        var result = await store.LoadAsync(CancellationToken.None);

        // Then
        result.ShouldBeOk().ShouldBe(initial);
    }

    [Fact]
    public async Task SaveAsync_PersistsConfigForNextLoad()
    {
        // Given
        var store = new FakeConfigStore();
        var toSave = ConfigDocument.Empty.AddDevice(MakeDevice()).ShouldBeOk();

        // When
        var save = await store.SaveAsync(toSave, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        // Then
        save.ShouldBeOk();
        loaded.ShouldBeOk().ShouldBe(toSave);
    }

    [Fact]
    public async Task SaveAsync_OverwritesPreviousState()
    {
        // Given
        var first = ConfigDocument.Empty.AddDevice(MakeDevice()).ShouldBeOk();
        var second = ConfigDocument.Empty;
        var store = new FakeConfigStore(first);

        // When
        var save = await store.SaveAsync(second, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        // Then
        save.ShouldBeOk();
        loaded.ShouldBeOk().ShouldBe(second);
    }

    [Fact]
    public async Task LoadAsync_WhenConfiguredToFail_ReturnsConfigStoreReadFailure()
    {
        // Given
        var store = new FakeConfigStore();
        store.FailNextLoadWith("disk gone");

        // When
        var result = await store.LoadAsync(CancellationToken.None);

        // Then
        var err = result.ShouldBeError().ShouldBeOfType<ConfigStoreReadFailure>();
        err.Reason.ShouldBe("disk gone");
    }

    [Fact]
    public async Task SaveAsync_WhenConfiguredToFail_ReturnsConfigStoreWriteFailure()
    {
        // Given
        var store = new FakeConfigStore();
        store.FailNextSaveWith("permission denied");

        // When
        var result = await store.SaveAsync(ConfigDocument.Empty, CancellationToken.None);

        // Then
        var err = result.ShouldBeError().ShouldBeOfType<ConfigStoreWriteFailure>();
        err.Reason.ShouldBe("permission denied");
    }
}
