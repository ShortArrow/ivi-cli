using System.Collections.Immutable;
using IviCli.Application.Audit;
using IviCli.Application.Capture;
using IviCli.Application.Devices;
using IviCli.Application.Mock;
using IviCli.Application.Servers;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Application.Tests.Audit;

/// <summary>
/// Locks in <see cref="ConfigMutated"/> emission for every handler
/// that mutates operator-managed persistent state (ADR 0043 Batch U).
/// One Theory body, eleven rows — one per handler. Each row runs the
/// real handler end-to-end against an in-memory store, then asserts
/// exactly one event was emitted with the expected Operation /
/// Target / Subject.
/// </summary>
public sealed class ConfigMutatedWiringTests
{
    [Theory]
    [InlineData("device.add", "device.add", "psu1")]
    [InlineData("device.remove", "device.remove", "psu1")]
    [InlineData("server.add", "server.add", "gw1")]
    [InlineData("server.remove", "server.remove", "gw1")]
    [InlineData("route.add", "route.add", "gw1/hislip0")]
    [InlineData("route.remove", "route.remove", "gw1/hislip0")]
    [InlineData("scene.add", "scene.add", "demo/*IDN?")]
    [InlineData("scene.remove", "scene.remove", "demo/1")]
    [InlineData("scenario.create", "scenario.create", "demo")]
    [InlineData("scenario.remove", "scenario.remove", "demo")]
    [InlineData("scenario.import", "scenario.import", "demo")]
    public async Task Handler_emits_ConfigMutated_with_expected_shape(
        string handlerKey,
        string expectedOperation,
        string expectedTarget
    )
    {
        var audit = new FakeAuditLog();
        var subject = new FakeAuditSubject("test");

        await DispatchAsync(handlerKey, audit, subject);

        var events = audit.Events.OfType<ConfigMutated>().ToArray();
        events.Length.ShouldBe(1, $"handler '{handlerKey}' should emit exactly one ConfigMutated");
        events[0].Operation.ShouldBe(expectedOperation);
        events[0].Target.ShouldBe(expectedTarget);
        events[0].Subject.ShouldBe("test");
    }

    private static async Task DispatchAsync(
        string handlerKey,
        FakeAuditLog audit,
        FakeAuditSubject subject
    )
    {
        switch (handlerKey)
        {
            case "device.add":
                await RunAddDevice(audit, subject);
                break;
            case "device.remove":
                await RunRemoveDevice(audit, subject);
                break;
            case "server.add":
                await RunAddServer(audit, subject);
                break;
            case "server.remove":
                await RunRemoveServer(audit, subject);
                break;
            case "route.add":
                await RunAddRoute(audit, subject);
                break;
            case "route.remove":
                await RunRemoveRoute(audit, subject);
                break;
            case "scene.add":
                await RunAddScene(audit, subject);
                break;
            case "scene.remove":
                await RunRemoveScene(audit, subject);
                break;
            case "scenario.create":
                await RunCreateScenario(audit, subject);
                break;
            case "scenario.remove":
                await RunRemoveScenario(audit, subject);
                break;
            case "scenario.import":
                await RunImportScenario(audit, subject);
                break;
            default:
                throw new InvalidOperationException($"unknown handler key: {handlerKey}");
        }
    }

    private static async Task RunAddDevice(FakeAuditLog audit, FakeAuditSubject subject)
    {
        var store = new FakeConfigStore();
        var handler = new AddDeviceCommandHandler(store, audit, subject);
        var result = await handler.HandleAsync(
            new AddDeviceCommand("psu1", "TCPIP0::1.2.3.4::inst0::INSTR", 3000),
            default
        );
        result.ShouldBeOk();
    }

    private static async Task RunRemoveDevice(FakeAuditLog audit, FakeAuditSubject subject)
    {
        var device = new Device(
            DeviceName.From("psu1").ShouldBeOk(),
            VisaResource.Parse("TCPIP0::1.2.3.4::inst0::INSTR").ShouldBeOk(),
            IviCli.Domain.Timeout.FromMilliseconds(3000).ShouldBeOk()
        );
        var seeded = ConfigDocument.Empty.AddDevice(device).ShouldBeOk();
        var store = new FakeConfigStore(seeded);
        var handler = new RemoveDeviceCommandHandler(store, audit, subject);
        var result = await handler.HandleAsync(new RemoveDeviceCommand("psu1"), default);
        result.ShouldBeOk();
    }

    private static async Task RunAddServer(FakeAuditLog audit, FakeAuditSubject subject)
    {
        var store = new FakeConfigStore();
        var handler = new AddServerCommandHandler(store, audit, subject);
        var result = await handler.HandleAsync(
            new AddServerCommand("gw1", "hislip", "127.0.0.1", 4880),
            default
        );
        result.ShouldBeOk();
    }

    private static async Task RunRemoveServer(FakeAuditLog audit, FakeAuditSubject subject)
    {
        var server = new Server(
            ServerName.From("gw1").ShouldBeOk(),
            ServerType.HiSlip,
            IpAddress.From("127.0.0.1").ShouldBeOk(),
            Port.From(4880).ShouldBeOk()
        );
        var seeded = ConfigDocument.Empty.AddServer(server).ShouldBeOk();
        var store = new FakeConfigStore(seeded);
        var handler = new RemoveServerCommandHandler(store, audit, subject);
        var result = await handler.HandleAsync(new RemoveServerCommand("gw1"), default);
        result.ShouldBeOk();
    }

    private static async Task RunAddRoute(FakeAuditLog audit, FakeAuditSubject subject)
    {
        var device = new Device(
            DeviceName.From("psu1").ShouldBeOk(),
            VisaResource.Parse("TCPIP0::1.2.3.4::inst0::INSTR").ShouldBeOk(),
            IviCli.Domain.Timeout.FromMilliseconds(3000).ShouldBeOk()
        );
        var server = new Server(
            ServerName.From("gw1").ShouldBeOk(),
            ServerType.HiSlip,
            IpAddress.From("127.0.0.1").ShouldBeOk(),
            Port.From(4880).ShouldBeOk()
        );
        var seeded = ConfigDocument
            .Empty.AddDevice(device)
            .ShouldBeOk()
            .AddServer(server)
            .ShouldBeOk();
        var store = new FakeConfigStore(seeded);
        var handler = new AddRouteCommandHandler(store, audit, subject);
        var result = await handler.HandleAsync(
            new AddRouteCommand("gw1", "hislip0", "psu1"),
            default
        );
        result.ShouldBeOk();
    }

    private static async Task RunRemoveRoute(FakeAuditLog audit, FakeAuditSubject subject)
    {
        var device = new Device(
            DeviceName.From("psu1").ShouldBeOk(),
            VisaResource.Parse("TCPIP0::1.2.3.4::inst0::INSTR").ShouldBeOk(),
            IviCli.Domain.Timeout.FromMilliseconds(3000).ShouldBeOk()
        );
        var server = new Server(
            ServerName.From("gw1").ShouldBeOk(),
            ServerType.HiSlip,
            IpAddress.From("127.0.0.1").ShouldBeOk(),
            Port.From(4880).ShouldBeOk()
        );
        var route = new Route(
            ServerName.From("gw1").ShouldBeOk(),
            PublicEndpoint.From("hislip0").ShouldBeOk(),
            DeviceName.From("psu1").ShouldBeOk()
        );
        var seeded = ConfigDocument
            .Empty.AddDevice(device)
            .ShouldBeOk()
            .AddServer(server)
            .ShouldBeOk()
            .AddRoute(route)
            .ShouldBeOk();
        var store = new FakeConfigStore(seeded);
        var handler = new RemoveRouteCommandHandler(store, audit, subject);
        var result = await handler.HandleAsync(new RemoveRouteCommand("gw1", "hislip0"), default);
        result.ShouldBeOk();
    }

    private static async Task RunAddScene(FakeAuditLog audit, FakeAuditSubject subject)
    {
        var name = ScenarioName.From("demo").ShouldBeOk();
        var store = new FakeScenarioStore(new[] { MockScenario.Empty(name) });
        var handler = new AddSceneCommandHandler(store, audit, subject);
        var result = await handler.HandleAsync(
            new AddSceneCommand(
                "demo",
                "*IDN?",
                Respond: "ACME,X,1,1.0",
                Ack: false,
                Fail: null,
                FailDetail: null
            ),
            default
        );
        result.ShouldBeOk();
    }

    private static async Task RunRemoveScene(FakeAuditLog audit, FakeAuditSubject subject)
    {
        var name = ScenarioName.From("demo").ShouldBeOk();
        var scenario = MockScenario
            .Empty(name)
            .AddScene(new MockScene("*IDN?", new SceneAction.Respond("ACME")));
        var store = new FakeScenarioStore(new[] { scenario });
        var handler = new RemoveSceneCommandHandler(store, audit, subject);
        var result = await handler.HandleAsync(new RemoveSceneCommand("demo", 1), default);
        result.ShouldBeOk();
    }

    private static async Task RunCreateScenario(FakeAuditLog audit, FakeAuditSubject subject)
    {
        var store = new FakeScenarioStore();
        var handler = new CreateScenarioCommandHandler(store, audit, subject);
        var result = await handler.HandleAsync(new CreateScenarioCommand("demo"), default);
        result.ShouldBeOk();
    }

    private static async Task RunRemoveScenario(FakeAuditLog audit, FakeAuditSubject subject)
    {
        var name = ScenarioName.From("demo").ShouldBeOk();
        var store = new FakeScenarioStore(new[] { MockScenario.Empty(name) });
        var handler = new RemoveScenarioCommandHandler(store, audit, subject);
        var result = await handler.HandleAsync(new RemoveScenarioCommand("demo"), default);
        result.ShouldBeOk();
    }

    private static async Task RunImportScenario(FakeAuditLog audit, FakeAuditSubject subject)
    {
        var t = new DateTimeOffset(2026, 5, 27, 12, 0, 0, TimeSpan.Zero);
        var events = new[]
        {
            new TrafficEvent(t, "psu1", TrafficOp.Open, null, null, true, 1, null),
            new TrafficEvent(t, "psu1", TrafficOp.Query, "*IDN?", "ACME", true, 1, null),
            new TrafficEvent(t, "psu1", TrafficOp.Close, null, null, true, 1, null),
        };
        var handler = new ImportScenarioFromTrafficCommandHandler(
            new InlineReader(events),
            new DefaultTrafficScenarioConverter(),
            new FakeScenarioStore(),
            audit,
            subject
        );
        var result = await handler.HandleAsync(
            new ImportScenarioFromTrafficCommand("ignored.ndjson", "demo", null, false),
            default
        );
        result.ShouldBeOk();
    }

    private sealed class InlineReader : INdjsonTrafficReader
    {
        private readonly ImmutableArray<TrafficEvent> _events;

        public InlineReader(IEnumerable<TrafficEvent> events) =>
            _events = events.ToImmutableArray();

        public async IAsyncEnumerable<TrafficEvent> ReadAsync(
            string path,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct
        )
        {
            foreach (var ev in _events)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return ev;
            }
        }
    }
}
