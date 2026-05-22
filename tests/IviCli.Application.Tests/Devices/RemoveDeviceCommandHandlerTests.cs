using IviCli.Application.Devices;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.TestKit;

namespace IviCli.Application.Tests.Devices;

public class RemoveDeviceCommandHandlerTests
{
    private static Device Dev(string name) =>
        new(
            DeviceName.From(name).ShouldBeOk(),
            VisaResource.Parse("TCPIP0::host::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    private static FakeConfigStore StoreWith(params Device[] devices)
    {
        var config = ConfigDocument.Empty;
        foreach (var d in devices)
        {
            config = config.AddDevice(d).ShouldBeOk();
        }
        return new FakeConfigStore(config);
    }

    [Fact]
    public async Task Handle_RemovesExistingDevice()
    {
        // Given
        var store = StoreWith(Dev("psu1"), Dev("scope1"));
        var handler = new RemoveDeviceCommandHandler(store);

        // When
        var result = await handler.HandleAsync(
            new RemoveDeviceCommand("psu1"),
            CancellationToken.None
        );

        // Then
        result.ShouldBeOk().Value.ShouldBe("psu1");
        var saved = (await store.LoadAsync(CancellationToken.None)).ShouldBeOk();
        saved.Devices.Length.ShouldBe(1);
        saved.Devices[0].Name.Value.ShouldBe("scope1");
    }

    [Fact]
    public async Task Handle_WithInvalidName_ReturnsRemoveDeviceInvalidName()
    {
        // Given
        var handler = new RemoveDeviceCommandHandler(new FakeConfigStore());

        // When
        var result = await handler.HandleAsync(
            new RemoveDeviceCommand("1bad-name"),
            CancellationToken.None
        );

        // Then
        var err = result.ShouldBeError().ShouldBeOfType<RemoveDeviceInvalidName>();
        err.Raw.ShouldBe("1bad-name");
    }

    [Fact]
    public async Task Handle_WithUnknownDevice_ReturnsRemoveDeviceNotFound()
    {
        // Given
        var handler = new RemoveDeviceCommandHandler(new FakeConfigStore());

        // When
        var result = await handler.HandleAsync(
            new RemoveDeviceCommand("ghost"),
            CancellationToken.None
        );

        // Then
        var err = result.ShouldBeError().ShouldBeOfType<RemoveDeviceNotFound>();
        err.Name.Value.ShouldBe("ghost");
    }

    [Fact]
    public async Task Handle_RemovingDefaultDevice_AlsoClearsDefault()
    {
        // Given
        var device = Dev("psu1");
        var seeded = ConfigDocument
            .Empty.AddDevice(device)
            .ShouldBeOk()
            .SetDefaultDevice(device.Name)
            .ShouldBeOk();
        var store = new FakeConfigStore(seeded);
        var handler = new RemoveDeviceCommandHandler(store);

        // When
        var result = await handler.HandleAsync(
            new RemoveDeviceCommand("psu1"),
            CancellationToken.None
        );

        // Then
        result.ShouldBeOk();
        var saved = (await store.LoadAsync(CancellationToken.None)).ShouldBeOk();
        saved.Devices.ShouldBeEmpty();
        saved.Defaults.Device.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenStoreLoadFails_ReturnsRemoveDeviceStorageFailure()
    {
        // Given
        var store = new FakeConfigStore();
        store.FailNextLoadWith("disk gone");
        var handler = new RemoveDeviceCommandHandler(store);

        // When
        var result = await handler.HandleAsync(
            new RemoveDeviceCommand("psu1"),
            CancellationToken.None
        );

        // Then
        result.ShouldBeError().ShouldBeOfType<RemoveDeviceStorageFailure>();
    }
}
