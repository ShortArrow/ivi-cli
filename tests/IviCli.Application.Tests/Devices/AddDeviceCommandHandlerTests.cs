using IviCli.Application.Devices;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.TestKit;

namespace IviCli.Application.Tests.Devices;

public class AddDeviceCommandHandlerTests
{
    private const string ValidName = "psu1";
    private const string ValidResource = "TCPIP0::192.168.0.10::inst0::INSTR";
    private const int ValidTimeout = 3000;

    private static AddDeviceCommand ValidCommand(string name = ValidName) =>
        new(name, ValidResource, ValidTimeout);

    [Fact]
    public async Task Handle_WithValidCommand_AddsDeviceAndReturnsName()
    {
        // Given
        var store = new FakeConfigStore();
        var handler = new AddDeviceCommandHandler(store);

        // When
        var result = await handler.HandleAsync(ValidCommand(), CancellationToken.None);

        // Then
        var name = result.ShouldBeOk();
        name.Value.ShouldBe(ValidName);

        var saved = (await store.LoadAsync(CancellationToken.None)).ShouldBeOk();
        saved.Devices.Length.ShouldBe(1);
        saved.Devices[0].Name.Value.ShouldBe(ValidName);
        saved.Devices[0].Resource.ShouldBeOfType<VisaResource.Tcpip>();
    }

    [Fact]
    public async Task Handle_WithInvalidName_ReturnsAddDeviceInvalidName()
    {
        // Given
        var handler = new AddDeviceCommandHandler(new FakeConfigStore());
        var command = ValidCommand(name: "1bad-name");

        // When
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Then
        var err = result.ShouldBeError().ShouldBeOfType<AddDeviceInvalidName>();
        err.Raw.ShouldBe("1bad-name");
    }

    [Fact]
    public async Task Handle_WithInvalidResource_ReturnsAddDeviceInvalidResource()
    {
        // Given
        var handler = new AddDeviceCommandHandler(new FakeConfigStore());
        var command = new AddDeviceCommand(ValidName, "not_a_resource", ValidTimeout);

        // When
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Then
        var err = result.ShouldBeError().ShouldBeOfType<AddDeviceInvalidResource>();
        err.Raw.ShouldBe("not_a_resource");
    }

    [Fact]
    public async Task Handle_WithInvalidTimeout_ReturnsAddDeviceInvalidTimeout()
    {
        // Given
        var handler = new AddDeviceCommandHandler(new FakeConfigStore());
        var command = new AddDeviceCommand(ValidName, ValidResource, -1);

        // When
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Then
        var err = result.ShouldBeError().ShouldBeOfType<AddDeviceInvalidTimeout>();
        err.RawMilliseconds.ShouldBe(-1);
    }

    [Fact]
    public async Task Handle_WithDuplicateName_ReturnsAddDeviceNameTaken()
    {
        // Given
        var store = new FakeConfigStore();
        var handler = new AddDeviceCommandHandler(store);
        await handler.HandleAsync(ValidCommand(), CancellationToken.None);

        // When (try to add the same name again)
        var result = await handler.HandleAsync(ValidCommand(), CancellationToken.None);

        // Then
        var err = result.ShouldBeError().ShouldBeOfType<AddDeviceNameTaken>();
        err.Name.Value.ShouldBe(ValidName);
    }

    [Fact]
    public async Task Handle_WhenStoreLoadFails_ReturnsAddDeviceStorageFailure()
    {
        // Given
        var store = new FakeConfigStore();
        store.FailNextLoadWith("disk gone");
        var handler = new AddDeviceCommandHandler(store);

        // When
        var result = await handler.HandleAsync(ValidCommand(), CancellationToken.None);

        // Then
        var err = result.ShouldBeError().ShouldBeOfType<AddDeviceStorageFailure>();
        err.Inner.ShouldBeOfType<Application.Configuration.ConfigStoreReadFailure>();
    }

    [Fact]
    public async Task Handle_WhenStoreSaveFails_ReturnsAddDeviceStorageFailure()
    {
        // Given
        var store = new FakeConfigStore();
        store.FailNextSaveWith("permission denied");
        var handler = new AddDeviceCommandHandler(store);

        // When
        var result = await handler.HandleAsync(ValidCommand(), CancellationToken.None);

        // Then
        var err = result.ShouldBeError().ShouldBeOfType<AddDeviceStorageFailure>();
        err.Inner.ShouldBeOfType<Application.Configuration.ConfigStoreWriteFailure>();
    }

    [Fact]
    public async Task Handle_WhenCancelled_ThrowsOperationCanceled()
    {
        // Given
        var handler = new AddDeviceCommandHandler(new FakeConfigStore());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // When / Then
        await Should.ThrowAsync<OperationCanceledException>(() =>
            handler.HandleAsync(ValidCommand(), cts.Token)
        );
    }
}
