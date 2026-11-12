using System.Collections.Immutable;
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
/// <c>mock serve</c> finds or registers a <c>MOCK::INSTR</c> device bound to
/// the scenario, a server and its one route, and reuses an entry only when
/// it is the one the command would make (ADR 0056 §1 to §4, §6). A failure
/// changes neither <c>config.toml</c> nor the session.
/// </summary>
public sealed class ServeMockCommandHandlerTests
{
    private const string Scenario = "my-dmm";
    private const int RunningPid = 4242;
    private const int StalePid = 4343;

    private sealed class Harness
    {
        public required ServeMockCommandHandler Handler { get; init; }
        public required FakeConfigStore Config { get; init; }
        public required FakeSessionStore Session { get; init; }
        public required FakeServerProcessRegistry Registry { get; init; }

        public async Task<ConfigDocument> SavedConfigAsync() =>
            (await Config.LoadAsync(CancellationToken.None)).ShouldBeOk();

        public async Task<SessionState> SavedSessionAsync() =>
            (await Session.LoadAsync(CancellationToken.None)).ShouldBeOk();

        /// <summary>Asserts that neither store differs from what it was seeded with.</summary>
        public async Task ShouldBeUnchangedAsync(ConfigDocument config, SessionState session)
        {
            (await SavedConfigAsync()).ShouldBe(config);
            var saved = await SavedSessionAsync();
            saved.CurrentDevice.ShouldBe(session.CurrentDevice);
            saved.DeviceScenarios.ShouldBe(session.DeviceScenarios, ignoreOrder: true);
        }
    }

    private static Harness Build(ConfigDocument? config = null, SessionState? session = null)
    {
        var configStore = new FakeConfigStore(config ?? ConfigDocument.Empty);
        var sessionStore = new FakeSessionStore(session ?? SessionState.Empty);
        var registry = new FakeServerProcessRegistry();
        var scenarios = new FakeScenarioStore(
            new[] { ScenarioNamed(Scenario), ScenarioNamed("other") }
        );
        return new Harness
        {
            Handler = new ServeMockCommandHandler(
                scenarios,
                configStore,
                sessionStore,
                registry,
                new FakeProcessLiveness(RunningPid)
            ),
            Config = configStore,
            Session = sessionStore,
            Registry = registry,
        };
    }

    private static MockScenario ScenarioNamed(string name) =>
        MockScenario.SingleScene(
            ScenarioName.From(name).ShouldBeOk(),
            idnDefault: null,
            rules: ImmutableArray<MockRule>.Empty
        );

    private static DeviceName Dev(string name) => DeviceName.From(name).ShouldBeOk();

    private static ServerName Srv(string name) => ServerName.From(name).ShouldBeOk();

    private static ScenarioName Scn(string name) => ScenarioName.From(name).ShouldBeOk();

    private static Device MockDevice(string name) =>
        new(
            Dev(name),
            VisaResource.Parse("MOCK::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    private static Server SocketServer(string name, int port = 5025, string bind = "127.0.0.1") =>
        new(
            Srv(name),
            ServerType.Socket,
            IpAddress.From(bind).ShouldBeOk(),
            Port.From(port).ShouldBeOk()
        );

    private static Route RouteOf(string server, string endpoint, string device) =>
        new(Srv(server), PublicEndpoint.From(endpoint).ShouldBeOk(), Dev(device));

    private static Task<IviCli.Domain.Result<ServedMock, ServeMockError>> Serve(
        Harness h,
        ServeMockCommand command
    ) => h.Handler.HandleAsync(command, CancellationToken.None);

    // ----- §1 and §2: what a first run registers ---------------------------------

    [Fact]
    public async Task A_first_run_registers_a_bound_mock_device_a_server_and_its_route()
    {
        // Given
        var h = Build();

        // When
        var served = (await Serve(h, new ServeMockCommand(Scenario, "socket"))).ShouldBeOk();

        // Then
        var config = await h.SavedConfigAsync();
        var device = config.FindDevice(Dev("mock-socket-5025")).ShouldNotBeNull();
        device.Resource.ShouldBeOfType<VisaResource.Mock>();
        device.Timeout.ShouldBe(Timeout.FromMilliseconds(3000).ShouldBeOk());
        config.FindServer(Srv("mock-socket-5025")).ShouldBe(SocketServer("mock-socket-5025"));
        config.Routes.ShouldBe([RouteOf("mock-socket-5025", "5025", "mock-socket-5025")]);
        (await h.SavedSessionAsync())
            .GetActiveScenario(Dev("mock-socket-5025"))
            .ShouldBe(Scn(Scenario));
        served.Device.ShouldBe(device);
        served.Server.ShouldBe(SocketServer("mock-socket-5025"));
        served.Route.ShouldBe(config.Routes[0]);
    }

    [Theory]
    [InlineData("socket", 5025, "5025")]
    [InlineData("hislip", 4880, "hislip0")]
    [InlineData("vxi11", 1024, "inst0")]
    [InlineData("usbip", 3240, "1-1")]
    public async Task Each_type_takes_its_default_port_and_endpoint(
        string type,
        int port,
        string endpoint
    )
    {
        var h = Build();

        var served = (await Serve(h, new ServeMockCommand(Scenario, type))).ShouldBeOk();

        served.Server.Port.Value.ShouldBe(port);
        served.Server.Name.Value.ShouldBe($"mock-{type}-{port}");
        served.Route.Endpoint.Value.ShouldBe(endpoint);
    }

    [Fact]
    public async Task A_socket_endpoint_follows_the_port_given()
    {
        var h = Build();

        var served = (
            await Serve(h, new ServeMockCommand(Scenario, "socket", Port: 6000))
        ).ShouldBeOk();

        served.Route.Endpoint.Value.ShouldBe("6000");
        served.Device.Name.Value.ShouldBe("mock-socket-6000");
    }

    [Fact]
    public async Task The_alias_names_the_device_and_the_server()
    {
        var h = Build();

        var served = (
            await Serve(h, new ServeMockCommand(Scenario, "hislip", Alias: "dmm"))
        ).ShouldBeOk();

        served.Device.Name.Value.ShouldBe("dmm");
        served.Server.Name.Value.ShouldBe("dmm");
        served.Route.ShouldBe(RouteOf("dmm", "hislip0", "dmm"));
    }

    [Fact]
    public async Task A_usbip_route_takes_the_profile_given()
    {
        var h = Build();

        var served = (
            await Serve(h, new ServeMockCommand(Scenario, "usbip", Profile: "cdc-acm"))
        ).ShouldBeOk();

        served.Route.Profile.ShouldBe(UsbExportProfile.CdcAcm);
    }

    [Fact]
    public async Task A_route_of_another_type_takes_the_default_profile_whichever_is_given()
    {
        var h = Build();

        var served = (
            await Serve(h, new ServeMockCommand(Scenario, "socket", Profile: "cdc-acm"))
        ).ShouldBeOk();

        served.Route.Profile.ShouldBe(UsbExportProfile.UsbTmc);
    }

    // ----- refused input ---------------------------------------------------------

    [Fact]
    public async Task A_profile_other_than_the_two_is_refused_for_every_type()
    {
        var h = Build();

        (await Serve(h, new ServeMockCommand(Scenario, "socket", Profile: "hid")))
            .ShouldBeError()
            .ShouldBeOfType<ServeMockInvalidProfile>()
            .Raw.ShouldBe("hid");
        await h.ShouldBeUnchangedAsync(ConfigDocument.Empty, SessionState.Empty);
    }

    [Theory]
    [InlineData("local")]
    [InlineData("gpib")]
    public async Task A_type_other_than_the_four_is_refused(string type)
    {
        var h = Build();

        (await Serve(h, new ServeMockCommand(Scenario, type)))
            .ShouldBeError()
            .ShouldBeOfType<ServeMockInvalidType>();
        await h.ShouldBeUnchangedAsync(ConfigDocument.Empty, SessionState.Empty);
    }

    [Fact]
    public async Task An_alias_that_is_not_a_valid_name_is_refused()
    {
        var h = Build();

        (await Serve(h, new ServeMockCommand(Scenario, "socket", Alias: "Bad Name")))
            .ShouldBeError()
            .ShouldBeOfType<ServeMockInvalidAlias>();
    }

    [Fact]
    public async Task The_management_api_name_is_refused()
    {
        var h = Build();

        (await Serve(h, new ServeMockCommand(Scenario, "socket", Alias: "ivi-management-api")))
            .ShouldBeError()
            .ShouldBeOfType<ServeMockReservedName>();
        await h.ShouldBeUnchangedAsync(ConfigDocument.Empty, SessionState.Empty);
    }

    [Fact]
    public async Task A_scenario_that_does_not_exist_is_refused()
    {
        var h = Build();

        (await Serve(h, new ServeMockCommand("missing", "socket")))
            .ShouldBeError()
            .ShouldBeOfType<ServeMockScenarioNotFound>();
        await h.ShouldBeUnchangedAsync(ConfigDocument.Empty, SessionState.Empty);
    }

    // ----- §3: a device with the name ------------------------------------------

    [Fact]
    public async Task A_second_run_reuses_every_entry_and_changes_nothing()
    {
        var h = Build();
        (await Serve(h, new ServeMockCommand(Scenario, "socket"))).ShouldBeOk();
        var config = await h.SavedConfigAsync();
        var session = await h.SavedSessionAsync();

        (await Serve(h, new ServeMockCommand(Scenario, "socket"))).ShouldBeOk();

        await h.ShouldBeUnchangedAsync(config, session);
    }

    [Fact]
    public async Task An_unbound_mock_device_is_bound_again()
    {
        var config = ConfigDocument.Empty.AddDevice(MockDevice("mock-socket-5025")).ShouldBeOk();
        var h = Build(config);

        (await Serve(h, new ServeMockCommand(Scenario, "socket"))).ShouldBeOk();

        (await h.SavedSessionAsync())
            .GetActiveScenario(Dev("mock-socket-5025"))
            .ShouldBe(Scn(Scenario));
    }

    [Fact]
    public async Task A_device_with_a_transport_is_refused_by_name()
    {
        var config = ConfigDocument
            .Empty.AddDevice(
                new Device(
                    Dev("dmm"),
                    VisaResource.Parse("TCPIP0::192.168.0.10::inst0::INSTR").ShouldBeOk(),
                    Timeout.FromMilliseconds(3000).ShouldBeOk()
                )
            )
            .ShouldBeOk();
        var h = Build(config);

        var conflict = (await Serve(h, new ServeMockCommand(Scenario, "socket", Alias: "dmm")))
            .ShouldBeError()
            .ShouldBeOfType<ServeMockDeviceConflict>();

        conflict.Name.ShouldBe(Dev("dmm"));
        conflict.Difference.ShouldContain("TCPIP0::192.168.0.10::inst0::INSTR");
        await h.ShouldBeUnchangedAsync(config, SessionState.Empty);
    }

    [Fact]
    public async Task A_mock_device_bound_to_another_scenario_is_refused()
    {
        var config = ConfigDocument.Empty.AddDevice(MockDevice("dmm")).ShouldBeOk();
        var session = SessionState.Empty.BindScenario(Dev("dmm"), Scn("other"));
        var h = Build(config, session);

        var conflict = (await Serve(h, new ServeMockCommand(Scenario, "socket", Alias: "dmm")))
            .ShouldBeError()
            .ShouldBeOfType<ServeMockDeviceConflict>();

        conflict.Difference.ShouldContain("other");
        await h.ShouldBeUnchangedAsync(config, session);
    }

    // ----- §4: a server with the name ------------------------------------------

    [Fact]
    public async Task A_matching_server_without_routes_gets_the_route()
    {
        var config = ConfigDocument.Empty.AddServer(SocketServer("dmm")).ShouldBeOk();
        var h = Build(config);

        (await Serve(h, new ServeMockCommand(Scenario, "socket", Alias: "dmm"))).ShouldBeOk();

        (await h.SavedConfigAsync()).Routes.ShouldBe([RouteOf("dmm", "5025", "dmm")]);
    }

    public static TheoryData<Server, string> MismatchedServers() =>
        new()
        {
            {
                new Server(
                    Srv("dmm"),
                    ServerType.HiSlip,
                    IpAddress.From("127.0.0.1").ShouldBeOk(),
                    Port.From(5025).ShouldBeOk()
                ),
                "hislip"
            },
            { SocketServer("dmm", port: 5026), "5026" },
            { SocketServer("dmm", bind: "0.0.0.0"), "0.0.0.0" },
        };

    [Theory]
    [MemberData(nameof(MismatchedServers))]
    public async Task A_server_that_differs_in_type_bind_or_port_is_refused(
        Server existing,
        string named
    )
    {
        var config = ConfigDocument.Empty.AddServer(existing).ShouldBeOk();
        var h = Build(config);

        var conflict = (await Serve(h, new ServeMockCommand(Scenario, "socket", Alias: "dmm")))
            .ShouldBeError()
            .ShouldBeOfType<ServeMockServerConflict>();

        conflict.Name.ShouldBe(Srv("dmm"));
        conflict.Difference.ShouldContain(named);
        await h.ShouldBeUnchangedAsync(config, SessionState.Empty);
    }

    [Fact]
    public async Task A_server_with_another_route_is_refused()
    {
        var config = ConfigDocument
            .Empty.AddDevice(MockDevice("dmm"))
            .ShouldBeOk()
            .AddServer(SocketServer("dmm"))
            .ShouldBeOk()
            .AddRoute(RouteOf("dmm", "5025", "dmm"))
            .ShouldBeOk()
            .AddRoute(RouteOf("dmm", "5026", "dmm"))
            .ShouldBeOk();
        var h = Build(config);

        (await Serve(h, new ServeMockCommand(Scenario, "socket", Alias: "dmm")))
            .ShouldBeError()
            .ShouldBeOfType<ServeMockServerConflict>()
            .Difference.ShouldContain("5026");
        await h.ShouldBeUnchangedAsync(config, SessionState.Empty);
    }

    [Fact]
    public async Task A_usbip_route_with_another_profile_is_refused()
    {
        var config = ConfigDocument
            .Empty.AddDevice(MockDevice("dmm"))
            .ShouldBeOk()
            .AddServer(
                new Server(
                    Srv("dmm"),
                    ServerType.UsbIp,
                    IpAddress.From("127.0.0.1").ShouldBeOk(),
                    Port.From(3240).ShouldBeOk()
                )
            )
            .ShouldBeOk()
            .AddRoute(RouteOf("dmm", "1-1", "dmm") with { Profile = UsbExportProfile.CdcAcm })
            .ShouldBeOk();
        var h = Build(config);

        (await Serve(h, new ServeMockCommand(Scenario, "usbip", Alias: "dmm")))
            .ShouldBeError()
            .ShouldBeOfType<ServeMockServerConflict>()
            .Difference.ShouldContain("cdc-acm");
        await h.ShouldBeUnchangedAsync(config, SessionState.Empty);
    }

    // ----- §6: a running server ------------------------------------------------

    [Fact]
    public async Task A_server_whose_recorded_process_runs_is_refused_and_its_record_kept()
    {
        var h = Build();
        await h.Registry.WriteAsync(
            Srv("mock-socket-5025"),
            RunningPid,
            DateTimeOffset.UnixEpoch,
            CancellationToken.None
        );

        (await Serve(h, new ServeMockCommand(Scenario, "socket")))
            .ShouldBeError()
            .ShouldBeOfType<ServeMockServerRunning>()
            .ProcessId.ShouldBe(RunningPid);
        await h.ShouldBeUnchangedAsync(ConfigDocument.Empty, SessionState.Empty);
        (await h.Registry.ReadAsync(Srv("mock-socket-5025"), CancellationToken.None))
            .ShouldBeOk()
            .ShouldNotBeNull();
    }

    [Fact]
    public async Task A_record_whose_process_is_gone_does_not_stop_the_run()
    {
        var h = Build();
        await h.Registry.WriteAsync(
            Srv("mock-socket-5025"),
            StalePid,
            DateTimeOffset.UnixEpoch,
            CancellationToken.None
        );

        (await Serve(h, new ServeMockCommand(Scenario, "socket"))).ShouldBeOk();
    }

    [Fact]
    public async Task A_record_that_cannot_be_read_is_refused()
    {
        var h = Build();
        h.Registry.FailNextReadWith("disk gone");

        (await Serve(h, new ServeMockCommand(Scenario, "socket")))
            .ShouldBeError()
            .ShouldBeOfType<ServeMockRegistryFailure>();
        await h.ShouldBeUnchangedAsync(ConfigDocument.Empty, SessionState.Empty);
    }

    // ----- storage --------------------------------------------------------------

    [Fact]
    public async Task A_failed_session_save_leaves_the_entries_registered()
    {
        var h = Build();
        h.Session.FailNextSaveWith("disk full");

        (await Serve(h, new ServeMockCommand(Scenario, "socket")))
            .ShouldBeError()
            .ShouldBeOfType<ServeMockSessionFailure>();
        (await h.SavedConfigAsync()).FindDevice(Dev("mock-socket-5025")).ShouldNotBeNull();
    }
}
