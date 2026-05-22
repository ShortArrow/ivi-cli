using IviCli.Application.Backends;
using IviCli.Application.Devices;
using IviCli.Backends.Fake;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Session;
using IviCli.Domain.Visa;
using IviCli.TestKit;

namespace IviCli.Application.Tests.Devices;

public class QueryDeviceCommandHandlerTests
{
    private static Device Dev(string name) =>
        new(
            DeviceName.From(name).ShouldBeOk(),
            VisaResource.Parse("TCPIP0::host::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    private static QueryDeviceCommandHandler MakeHandler(
        out FakeConfigStore config,
        out FakeSessionStore session,
        out FakeBackend backend,
        params Device[] seedDevices
    )
    {
        var doc = ConfigDocument.Empty;
        foreach (var d in seedDevices)
        {
            doc = doc.AddDevice(d).ShouldBeOk();
        }
        config = new FakeConfigStore(doc);
        session = new FakeSessionStore();
        backend = new FakeBackend();
        var factory = new FakeBackendFactory(backend);
        return new QueryDeviceCommandHandler(config, session, factory);
    }

    [Fact]
    public async Task Handle_ExplicitDevice_ReturnsResponse()
    {
        // Given
        var handler = MakeHandler(out var _, out var _, out var backend, Dev("psu1"));
        backend.ConfigureDevice(DeviceName.From("psu1").ShouldBeOk(), "ACME,MOD,001,1.0");

        // When
        var result = await handler.HandleAsync(
            new QueryDeviceCommand("psu1", "*IDN?"),
            CancellationToken.None
        );

        // Then
        result.ShouldBeOk().ShouldBe("ACME,MOD,001,1.0");
    }

    [Fact]
    public async Task Handle_ImplicitCurrentDevice_ReturnsResponse()
    {
        // Given
        var handler = MakeHandler(out var _, out var session, out var backend, Dev("psu1"));
        await session.SaveAsync(
            new SessionState(DeviceName.From("psu1").ShouldBeOk()),
            CancellationToken.None
        );

        // When
        var result = await handler.HandleAsync(
            new QueryDeviceCommand(Name: null, "*IDN?"),
            CancellationToken.None
        );

        // Then
        result.ShouldBeOk();
    }

    [Fact]
    public async Task Handle_NoTarget_ReturnsNoTarget()
    {
        // Given
        var handler = MakeHandler(out var _, out var _, out var _);

        // When
        var result = await handler.HandleAsync(
            new QueryDeviceCommand(Name: null, "*IDN?"),
            CancellationToken.None
        );

        // Then
        result.ShouldBeError().ShouldBeOfType<QueryDeviceNoTarget>();
    }

    [Fact]
    public async Task Handle_InvalidScpi_ReturnsInvalidScpi()
    {
        // Given
        var handler = MakeHandler(out var _, out var _, out var _, Dev("psu1"));

        // When
        var result = await handler.HandleAsync(
            new QueryDeviceCommand("psu1", "OUTP ON"),
            CancellationToken.None
        );

        // Then
        result.ShouldBeError().ShouldBeOfType<QueryDeviceInvalidScpi>();
    }

    [Fact]
    public async Task Handle_UnknownDevice_ReturnsUnknown()
    {
        // Given
        var handler = MakeHandler(out var _, out var _, out var _);

        // When
        var result = await handler.HandleAsync(
            new QueryDeviceCommand("ghost", "*IDN?"),
            CancellationToken.None
        );

        // Then
        result.ShouldBeError().ShouldBeOfType<QueryDeviceUnknown>();
    }

    [Fact]
    public async Task Handle_BackendTimeout_ReturnsTransportFailure()
    {
        // Given
        var handler = MakeHandler(out var _, out var _, out var backend, Dev("psu1"));
        backend.FailQuery(
            DeviceName.From("psu1").ShouldBeOk(),
            "*IDN?",
            new TransportTimeout(TimeSpan.FromMilliseconds(50))
        );

        // When
        var result = await handler.HandleAsync(
            new QueryDeviceCommand("psu1", "*IDN?"),
            CancellationToken.None
        );

        // Then
        result.ShouldBeError().ShouldBeOfType<QueryDeviceTransportFailure>();
    }
}
