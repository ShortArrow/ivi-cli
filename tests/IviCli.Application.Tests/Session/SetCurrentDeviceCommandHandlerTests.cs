using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Session;
using IviCli.Domain.Visa;
using IviCli.TestKit;

namespace IviCli.Application.Tests.Session;

public class SetCurrentDeviceCommandHandlerTests
{
    private static Device Dev(string name) =>
        new(
            DeviceName.From(name).ShouldBeOk(),
            VisaResource.Parse("TCPIP0::host::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    private static (FakeConfigStore config, FakeSessionStore session) StoresWith(
        params string[] names
    )
    {
        var doc = ConfigDocument.Empty;
        foreach (var n in names)
        {
            doc = doc.AddDevice(Dev(n)).ShouldBeOk();
        }
        return (new FakeConfigStore(doc), new FakeSessionStore());
    }

    [Fact]
    public async Task Handle_WithExistingDevice_UpdatesSessionOnly()
    {
        // Given
        var (config, session) = StoresWith("psu1", "scope1");
        var handler = new SetCurrentDeviceCommandHandler(config, session);

        // When
        var result = await handler.HandleAsync(
            new SetCurrentDeviceCommand("psu1", Persist: false),
            CancellationToken.None
        );

        // Then
        result.ShouldBeOk().Value.ShouldBe("psu1");
        (await session.LoadAsync(CancellationToken.None))
            .ShouldBeOk()
            .CurrentDevice!.Value.ShouldBe("psu1");
        (await config.LoadAsync(CancellationToken.None))
            .ShouldBeOk()
            .Defaults.Device.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WithPersist_AlsoUpdatesConfigDefaults()
    {
        // Given
        var (config, session) = StoresWith("psu1");
        var handler = new SetCurrentDeviceCommandHandler(config, session);

        // When
        var result = await handler.HandleAsync(
            new SetCurrentDeviceCommand("psu1", Persist: true),
            CancellationToken.None
        );

        // Then
        result.ShouldBeOk();
        (await session.LoadAsync(CancellationToken.None))
            .ShouldBeOk()
            .CurrentDevice!.Value.ShouldBe("psu1");
        (await config.LoadAsync(CancellationToken.None))
            .ShouldBeOk()
            .Defaults.Device!.Value.ShouldBe("psu1");
    }

    [Fact]
    public async Task Handle_WithInvalidName_ReturnsInvalidNameError()
    {
        // Given
        var (config, session) = StoresWith();
        var handler = new SetCurrentDeviceCommandHandler(config, session);

        // When
        var result = await handler.HandleAsync(
            new SetCurrentDeviceCommand("1bad-name", Persist: false),
            CancellationToken.None
        );

        // Then
        result.ShouldBeError().ShouldBeOfType<SetCurrentDeviceInvalidName>();
    }

    [Fact]
    public async Task Handle_WithUnknownDevice_ReturnsUnknown()
    {
        // Given
        var (config, session) = StoresWith("psu1");
        var handler = new SetCurrentDeviceCommandHandler(config, session);

        // When
        var result = await handler.HandleAsync(
            new SetCurrentDeviceCommand("ghost", Persist: false),
            CancellationToken.None
        );

        // Then
        var err = result.ShouldBeError().ShouldBeOfType<SetCurrentDeviceUnknown>();
        err.Name.Value.ShouldBe("ghost");
    }

    [Fact]
    public async Task Handle_WhenSessionSaveFails_ReturnsSessionFailure()
    {
        // Given
        var (config, session) = StoresWith("psu1");
        session.FailNextSaveWith("permission denied");
        var handler = new SetCurrentDeviceCommandHandler(config, session);

        // When
        var result = await handler.HandleAsync(
            new SetCurrentDeviceCommand("psu1", Persist: false),
            CancellationToken.None
        );

        // Then
        result.ShouldBeError().ShouldBeOfType<SetCurrentDeviceSessionFailure>();
    }
}
