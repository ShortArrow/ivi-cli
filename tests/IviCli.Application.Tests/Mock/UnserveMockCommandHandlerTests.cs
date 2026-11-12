using IviCli.Application.Mock;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;
using IviCli.Domain.Servers;
using IviCli.Domain.Session;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Application.Tests.Mock;

/// <summary>
/// <c>mock unserve</c> removes the <c>MOCK::INSTR</c> device with the name,
/// its binding, and the server with the name with its route, and removes
/// nothing unless each is the one <c>mock serve</c> would have made and no
/// process runs under the name (ADR 0056 §7).
/// </summary>
public sealed class UnserveMockCommandHandlerTests
{
    private const string Name = "dmm";
    private const int RunningPid = 4242;
    private const int StalePid = 4343;

    private sealed class Harness
    {
        public required UnserveMockCommandHandler Handler { get; init; }
        public required FakeConfigStore Config { get; init; }
        public required FakeSessionStore Session { get; init; }
        public required FakeServerProcessRegistry Registry { get; init; }
        public required FakeAuditLog Audit { get; init; }

        public Task<IviCli.Domain.Result<UnservedMock, UnserveMockError>> Unserve(
            string name = Name
        ) => Handler.HandleAsync(new UnserveMockCommand(name), CancellationToken.None);

        public async Task<ConfigDocument> SavedConfigAsync() =>
            (await Config.LoadAsync(CancellationToken.None)).ShouldBeOk();

        public async Task<SessionState> SavedSessionAsync() =>
            (await Session.LoadAsync(CancellationToken.None)).ShouldBeOk();

        public async Task RecordAsync(int pid) =>
            (
                await Registry.WriteAsync(
                    Srv(Name),
                    pid,
                    DateTimeOffset.UnixEpoch,
                    CancellationToken.None
                )
            ).ShouldBeOk();

        /// <summary>Asserts that neither store differs from what it was seeded with.</summary>
        public async Task ShouldBeUnchangedAsync(ConfigDocument config, SessionState session)
        {
            (await SavedConfigAsync()).ShouldBe(config);
            var saved = await SavedSessionAsync();
            saved.CurrentDevice.ShouldBe(session.CurrentDevice);
            saved.DeviceScenarios.ShouldBe(session.DeviceScenarios, ignoreOrder: true);
            Audit.Events.ShouldBeEmpty();
        }
    }

    private static Harness Build(ConfigDocument config, SessionState? session = null)
    {
        var configStore = new FakeConfigStore(config);
        var sessionStore = new FakeSessionStore(session ?? Bound);
        var registry = new FakeServerProcessRegistry();
        var audit = new FakeAuditLog();
        return new Harness
        {
            Handler = new UnserveMockCommandHandler(
                configStore,
                sessionStore,
                registry,
                new FakeProcessLiveness(RunningPid),
                audit,
                new FakeAuditSubject("test")
            ),
            Config = configStore,
            Session = sessionStore,
            Registry = registry,
            Audit = audit,
        };
    }

    private static DeviceName Dev(string name) => DeviceName.From(name).ShouldBeOk();

    private static ServerName Srv(string name) => ServerName.From(name).ShouldBeOk();

    private static SessionState Bound { get; } =
        SessionState.Empty.BindScenario(Dev(Name), ScenarioName.From("my-dmm").ShouldBeOk());

    private static Device DeviceOf(string name, string resource) =>
        new(
            Dev(name),
            VisaResource.Parse(resource).ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    private static Server ServerOf(string name, ServerType type, int port) =>
        new(
            Srv(name),
            type,
            IpAddress.From("127.0.0.1").ShouldBeOk(),
            Port.From(port).ShouldBeOk()
        );

    private static Route RouteOf(string server, string endpoint, string device) =>
        new(Srv(server), PublicEndpoint.From(endpoint).ShouldBeOk(), Dev(device));

    /// <summary>What <c>mock serve my-dmm --type socket --alias dmm</c> registers.</summary>
    private static ConfigDocument Served() =>
        ConfigDocument
            .Empty.AddDevice(DeviceOf(Name, "MOCK::INSTR"))
            .ShouldBeOk()
            .AddServer(ServerOf(Name, ServerType.Socket, 5025))
            .ShouldBeOk()
            .AddRoute(RouteOf(Name, "5025", Name))
            .ShouldBeOk();

    // ----- what it removes -----------------------------------------------------

    [Fact]
    public async Task It_removes_the_device_its_binding_the_server_and_its_route()
    {
        // Given
        var h = Build(Served());

        // When
        var unserved = (await h.Unserve()).ShouldBeOk();

        // Then
        unserved.ShouldBe(new UnservedMock(Dev(Name), Srv(Name)));
        var config = await h.SavedConfigAsync();
        config.Devices.ShouldBeEmpty();
        config.Servers.ShouldBeEmpty();
        config.Routes.ShouldBeEmpty();
        (await h.SavedSessionAsync()).GetActiveScenario(Dev(Name)).ShouldBeNull();
    }

    [Theory]
    [InlineData(ServerType.Socket, 6000, "6000")]
    [InlineData(ServerType.HiSlip, 4880, "hislip0")]
    [InlineData(ServerType.Vxi11, 1024, "inst0")]
    [InlineData(ServerType.UsbIp, 3240, "1-1")]
    public async Task It_removes_a_server_of_each_type_at_its_endpoint(
        ServerType type,
        int port,
        string endpoint
    )
    {
        var config = ConfigDocument
            .Empty.AddDevice(DeviceOf(Name, "MOCK::INSTR"))
            .ShouldBeOk()
            .AddServer(ServerOf(Name, type, port))
            .ShouldBeOk()
            .AddRoute(RouteOf(Name, endpoint, Name))
            .ShouldBeOk();
        var h = Build(config);

        (await h.Unserve()).ShouldBeOk();

        (await h.SavedConfigAsync()).Servers.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_server_without_routes_is_removed()
    {
        var config = ConfigDocument
            .Empty.AddDevice(DeviceOf(Name, "MOCK::INSTR"))
            .ShouldBeOk()
            .AddServer(ServerOf(Name, ServerType.Socket, 5025))
            .ShouldBeOk();
        var h = Build(config);

        (await h.Unserve()).ShouldBeOk();

        (await h.SavedConfigAsync()).Servers.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_server_the_device_and_its_binding_are_still_removed()
    {
        var h = Build(ConfigDocument.Empty.AddDevice(DeviceOf(Name, "MOCK::INSTR")).ShouldBeOk());

        (await h.Unserve()).ShouldBeOk().Server.ShouldBeNull();

        (await h.SavedConfigAsync()).Devices.ShouldBeEmpty();
        (await h.SavedSessionAsync()).GetActiveScenario(Dev(Name)).ShouldBeNull();
    }

    [Fact]
    public async Task The_binding_is_removed_whatever_scenario_it_names()
    {
        var session = SessionState.Empty.BindScenario(
            Dev(Name),
            ScenarioName.From("other").ShouldBeOk()
        );
        var h = Build(Served(), session);

        (await h.Unserve()).ShouldBeOk();

        (await h.SavedSessionAsync()).DeviceScenarios.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_default_device_is_cleared_when_it_named_the_device()
    {
        var h = Build(Served().SetDefaultDevice(Dev(Name)).ShouldBeOk());

        (await h.Unserve()).ShouldBeOk();

        (await h.SavedConfigAsync()).Defaults.Device.ShouldBeNull();
    }

    [Fact]
    public async Task A_record_whose_process_is_gone_is_removed_with_the_server()
    {
        var h = Build(Served());
        await h.RecordAsync(StalePid);

        (await h.Unserve()).ShouldBeOk();

        (await h.Registry.ReadAsync(Srv(Name), CancellationToken.None)).ShouldBeOk().ShouldBeNull();
    }

    [Fact]
    public async Task It_records_one_config_change()
    {
        var h = Build(Served());

        (await h.Unserve()).ShouldBeOk();

        var mutated = h
            .Audit.Events.OfType<IviCli.Application.Audit.ConfigMutated>()
            .ShouldHaveSingleItem();
        mutated.Operation.ShouldBe("mock.unserve");
        mutated.Target.ShouldBe(Name);
    }

    // ----- what it refuses -----------------------------------------------------

    [Fact]
    public async Task A_name_with_no_device_is_refused()
    {
        var config = ConfigDocument
            .Empty.AddServer(ServerOf(Name, ServerType.Socket, 5025))
            .ShouldBeOk();
        var h = Build(config);

        (await h.Unserve()).ShouldBeError().ShouldBeOfType<UnserveMockDeviceNotFound>();
        await h.ShouldBeUnchangedAsync(config, Bound);
    }

    [Fact]
    public async Task An_invalid_name_is_refused()
    {
        var h = Build(Served());

        (await h.Unserve("Bad Name")).ShouldBeError().ShouldBeOfType<UnserveMockInvalidName>();
        await h.ShouldBeUnchangedAsync(Served(), Bound);
    }

    [Fact]
    public async Task A_device_with_a_transport_is_refused()
    {
        var config = ConfigDocument
            .Empty.AddDevice(DeviceOf(Name, "TCPIP0::192.168.0.10::inst0::INSTR"))
            .ShouldBeOk();
        var h = Build(config);

        (await h.Unserve())
            .ShouldBeError()
            .ShouldBeOfType<UnserveMockDeviceConflict>()
            .Difference.ShouldContain("TCPIP0::192.168.0.10::inst0::INSTR");
        await h.ShouldBeUnchangedAsync(config, Bound);
    }

    [Fact]
    public async Task A_device_another_server_routes_to_is_refused()
    {
        var config = Served()
            .AddServer(ServerOf("bench", ServerType.HiSlip, 4880))
            .ShouldBeOk()
            .AddRoute(RouteOf("bench", "hislip1", Name))
            .ShouldBeOk();
        var h = Build(config);

        (await h.Unserve())
            .ShouldBeError()
            .ShouldBeOfType<UnserveMockDeviceConflict>()
            .Difference.ShouldContain("bench");
        await h.ShouldBeUnchangedAsync(config, Bound);
    }

    [Fact]
    public async Task A_server_of_another_type_is_refused()
    {
        var config = ConfigDocument
            .Empty.AddDevice(DeviceOf(Name, "MOCK::INSTR"))
            .ShouldBeOk()
            .AddServer(ServerOf(Name, ServerType.Local, 5025))
            .ShouldBeOk();
        var h = Build(config);

        (await h.Unserve()).ShouldBeError().ShouldBeOfType<UnserveMockServerConflict>();
        await h.ShouldBeUnchangedAsync(config, Bound);
    }

    [Fact]
    public async Task A_server_with_two_routes_is_refused()
    {
        var config = Served().AddRoute(RouteOf(Name, "5026", Name)).ShouldBeOk();
        var h = Build(config);

        (await h.Unserve()).ShouldBeError().ShouldBeOfType<UnserveMockServerConflict>();
        await h.ShouldBeUnchangedAsync(config, Bound);
    }

    [Fact]
    public async Task A_server_whose_route_is_not_the_types_endpoint_is_refused()
    {
        var config = ConfigDocument
            .Empty.AddDevice(DeviceOf(Name, "MOCK::INSTR"))
            .ShouldBeOk()
            .AddServer(ServerOf(Name, ServerType.Socket, 5025))
            .ShouldBeOk()
            .AddRoute(RouteOf(Name, "5026", Name))
            .ShouldBeOk();
        var h = Build(config);

        (await h.Unserve())
            .ShouldBeError()
            .ShouldBeOfType<UnserveMockServerConflict>()
            .Difference.ShouldContain("5026");
        await h.ShouldBeUnchangedAsync(config, Bound);
    }

    [Fact]
    public async Task A_server_whose_route_goes_to_another_device_is_refused()
    {
        var config = ConfigDocument
            .Empty.AddDevice(DeviceOf(Name, "MOCK::INSTR"))
            .ShouldBeOk()
            .AddDevice(DeviceOf("psu", "TCPIP0::192.168.0.11::inst0::INSTR"))
            .ShouldBeOk()
            .AddServer(ServerOf(Name, ServerType.Socket, 5025))
            .ShouldBeOk()
            .AddRoute(RouteOf(Name, "5025", "psu"))
            .ShouldBeOk();
        var h = Build(config);

        (await h.Unserve())
            .ShouldBeError()
            .ShouldBeOfType<UnserveMockServerConflict>()
            .Difference.ShouldContain("psu");
        await h.ShouldBeUnchangedAsync(config, Bound);
    }

    [Fact]
    public async Task A_running_server_is_refused_and_its_record_kept()
    {
        var h = Build(Served());
        await h.RecordAsync(RunningPid);

        (await h.Unserve())
            .ShouldBeError()
            .ShouldBeOfType<UnserveMockServerRunning>()
            .ProcessId.ShouldBe(RunningPid);
        await h.ShouldBeUnchangedAsync(Served(), Bound);
        (await h.Registry.ReadAsync(Srv(Name), CancellationToken.None))
            .ShouldBeOk()
            .ShouldNotBeNull();
    }

    [Fact]
    public async Task A_running_process_is_refused_when_the_server_entry_is_already_gone()
    {
        var config = ConfigDocument.Empty.AddDevice(DeviceOf(Name, "MOCK::INSTR")).ShouldBeOk();
        var h = Build(config);
        await h.RecordAsync(RunningPid);

        (await h.Unserve()).ShouldBeError().ShouldBeOfType<UnserveMockServerRunning>();
        await h.ShouldBeUnchangedAsync(config, Bound);
    }

    [Fact]
    public async Task A_record_that_cannot_be_read_is_refused()
    {
        var h = Build(Served());
        h.Registry.FailNextReadWith("disk gone");

        (await h.Unserve()).ShouldBeError().ShouldBeOfType<UnserveMockRegistryFailure>();
        await h.ShouldBeUnchangedAsync(Served(), Bound);
    }
}
