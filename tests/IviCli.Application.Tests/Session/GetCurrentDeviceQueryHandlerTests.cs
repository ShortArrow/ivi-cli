using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Session;
using IviCli.Domain.Visa;
using IviCli.TestKit;

namespace IviCli.Application.Tests.Session;

public class GetCurrentDeviceQueryHandlerTests
{
    private static Device Dev(string name) =>
        new(
            DeviceName.From(name).ShouldBeOk(),
            VisaResource.Parse("TCPIP0::host::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    [Fact]
    public async Task Handle_WhenSessionHasCurrentDevice_ReturnsIt()
    {
        // Given
        var session = new FakeSessionStore(
            new SessionState(
                DeviceName.From("psu1").ShouldBeOk(),
                System
                    .Collections
                    .Immutable
                    .ImmutableDictionary<DeviceName, IviCli.Domain.Mock.ScenarioName>
                    .Empty
            )
        );
        var config = new FakeConfigStore();
        var handler = new GetCurrentDeviceQueryHandler(config, session);

        // When
        var result = await handler.HandleAsync(new GetCurrentDeviceQuery(), CancellationToken.None);

        // Then
        result.ShouldBeOk().Name!.Value.ShouldBe("psu1");
    }

    [Fact]
    public async Task Handle_WhenSessionEmpty_FallsBackToConfigDefault()
    {
        // Given
        var configDoc = ConfigDocument
            .Empty.AddDevice(Dev("psu1"))
            .ShouldBeOk()
            .SetDefaultDevice(DeviceName.From("psu1").ShouldBeOk())
            .ShouldBeOk();
        var session = new FakeSessionStore();
        var config = new FakeConfigStore(configDoc);
        var handler = new GetCurrentDeviceQueryHandler(config, session);

        // When
        var result = await handler.HandleAsync(new GetCurrentDeviceQuery(), CancellationToken.None);

        // Then
        result.ShouldBeOk().Name!.Value.ShouldBe("psu1");
    }

    [Fact]
    public async Task Handle_WhenNothingSet_ReturnsNullName()
    {
        // Given
        var handler = new GetCurrentDeviceQueryHandler(
            new FakeConfigStore(),
            new FakeSessionStore()
        );

        // When
        var result = await handler.HandleAsync(new GetCurrentDeviceQuery(), CancellationToken.None);

        // Then
        result.ShouldBeOk().Name.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenSessionLoadFails_ReturnsSessionFailure()
    {
        // Given
        var session = new FakeSessionStore();
        session.FailNextLoadWith("disk gone");
        var handler = new GetCurrentDeviceQueryHandler(new FakeConfigStore(), session);

        // When
        var result = await handler.HandleAsync(new GetCurrentDeviceQuery(), CancellationToken.None);

        // Then
        result.ShouldBeError().ShouldBeOfType<GetCurrentDeviceSessionFailure>();
    }
}
