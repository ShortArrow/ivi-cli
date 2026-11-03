using IviCli.Application.Devices;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;
using IviCli.Domain.Session;
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
        var handler = new RemoveDeviceCommandHandler(store, new FakeSessionStore());

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
        var handler = new RemoveDeviceCommandHandler(new FakeConfigStore(), new FakeSessionStore());

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
        var handler = new RemoveDeviceCommandHandler(new FakeConfigStore(), new FakeSessionStore());

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
        var handler = new RemoveDeviceCommandHandler(store, new FakeSessionStore());

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
        var handler = new RemoveDeviceCommandHandler(store, new FakeSessionStore());

        // When
        var result = await handler.HandleAsync(
            new RemoveDeviceCommand("psu1"),
            CancellationToken.None
        );

        // Then
        result.ShouldBeError().ShouldBeOfType<RemoveDeviceStorageFailure>();
    }

    private static DeviceName Name(string name) => DeviceName.From(name).ShouldBeOk();

    private static FakeSessionStore SessionBinding(params string[] devices)
    {
        var state = SessionState.Empty;
        foreach (var d in devices)
        {
            state = state.BindScenario(Name(d), ScenarioName.From("fsm").ShouldBeOk());
        }
        return new FakeSessionStore(state);
    }

    [Fact]
    public async Task Handle_ClearsTheRemovedDevicesScenarioBindingAndNoOther()
    {
        // Given
        var session = SessionBinding("psu1", "scope1");
        var handler = new RemoveDeviceCommandHandler(
            StoreWith(Dev("psu1"), Dev("scope1")),
            session
        );

        // When
        var result = await handler.HandleAsync(
            new RemoveDeviceCommand("psu1"),
            CancellationToken.None
        );

        // Then
        result.ShouldBeOk();
        var state = (await session.LoadAsync(CancellationToken.None)).ShouldBeOk();
        state.GetActiveScenario(Name("psu1")).ShouldBeNull();
        state.GetActiveScenario(Name("scope1")).ShouldNotBeNull();
    }

    [Fact]
    public async Task Handle_WhenNoDeviceHasThatName_LeavesTheBindingAlone()
    {
        // Given
        var session = SessionBinding("psu1");
        var handler = new RemoveDeviceCommandHandler(new FakeConfigStore(), session);

        // When
        var result = await handler.HandleAsync(
            new RemoveDeviceCommand("psu1"),
            CancellationToken.None
        );

        // Then
        result.ShouldBeError().ShouldBeOfType<RemoveDeviceNotFound>();
        var state = (await session.LoadAsync(CancellationToken.None)).ShouldBeOk();
        state.GetActiveScenario(Name("psu1")).ShouldNotBeNull();
    }

    [Fact]
    public async Task Handle_WhenTheConfigSaveFails_LeavesTheBindingAlone()
    {
        // Given
        var store = StoreWith(Dev("psu1"));
        store.FailNextSaveWith("disk full");
        var session = SessionBinding("psu1");
        var handler = new RemoveDeviceCommandHandler(store, session);

        // When
        var result = await handler.HandleAsync(
            new RemoveDeviceCommand("psu1"),
            CancellationToken.None
        );

        // Then
        result.ShouldBeError().ShouldBeOfType<RemoveDeviceStorageFailure>();
        var state = (await session.LoadAsync(CancellationToken.None)).ShouldBeOk();
        state.GetActiveScenario(Name("psu1")).ShouldNotBeNull();
    }

    [Fact]
    public async Task Handle_WhenTheSessionCannotBeRead_RemovesNothing()
    {
        // Given
        var store = StoreWith(Dev("psu1"));
        var session = SessionBinding("psu1");
        session.FailNextLoadWith("corrupt");
        var handler = new RemoveDeviceCommandHandler(store, session);

        // When
        var result = await handler.HandleAsync(
            new RemoveDeviceCommand("psu1"),
            CancellationToken.None
        );

        // Then
        result.ShouldBeError().ShouldBeOfType<RemoveDeviceSessionFailure>();
        var saved = (await store.LoadAsync(CancellationToken.None)).ShouldBeOk();
        saved.Devices.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Handle_WhenTheBindingCannotBeCleared_ReportsTheRemovedDeviceAndAudits()
    {
        // Given
        var store = StoreWith(Dev("psu1"));
        var session = SessionBinding("psu1");
        session.FailNextSaveWith("disk full");
        var audit = new FakeAuditLog();
        var handler = new RemoveDeviceCommandHandler(store, session, audit);

        // When
        var result = await handler.HandleAsync(
            new RemoveDeviceCommand("psu1"),
            CancellationToken.None
        );

        // Then
        result
            .ShouldBeError()
            .ShouldBeOfType<RemoveDeviceBindingNotCleared>()
            .Name.ShouldBe(Name("psu1"));
        var saved = (await store.LoadAsync(CancellationToken.None)).ShouldBeOk();
        saved.Devices.ShouldBeEmpty();
        audit.Events.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Handle_WhenTheDeviceHasNoBinding_DoesNotWriteTheSession()
    {
        // Given
        var session = new FakeSessionStore();
        session.FailNextSaveWith("disk full");
        var handler = new RemoveDeviceCommandHandler(StoreWith(Dev("psu1")), session);

        // When
        var result = await handler.HandleAsync(
            new RemoveDeviceCommand("psu1"),
            CancellationToken.None
        );

        // Then
        result.ShouldBeOk();
    }
}
