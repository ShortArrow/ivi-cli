using IviCli.Application.Audit;
using IviCli.Application.Configuration;
using IviCli.Application.Servers;
using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;
using IviCli.Domain.Protocols;
using IviCli.Domain.Servers;
using IviCli.Domain.Session;
using IviCli.Domain.Visa;

namespace IviCli.Application.Mock;

/// <summary>
/// Application-layer handler for <c>mock serve</c>: finds or registers the
/// <c>MOCK::INSTR</c> device, the server and its route that serve a
/// scenario, and binds the scenario to the device (ADR 0056).
/// </summary>
/// <remarks>
/// <para>
/// Every check runs before anything is written, so a failure leaves
/// <c>config.toml</c> and the session as they were. An entry with the name
/// is reused only when it is the one this command would make: a device
/// whose resource is <c>MOCK::INSTR</c> and whose binding names no other
/// scenario (§3), and a server of the same type, bind address and port
/// whose only route is the type's endpoint to that device (§4). A server
/// whose recorded process is running is refused (§6).
/// </para>
/// <para>
/// A device this command registers has the timeout <c>visa add</c> gives by
/// default, 3000 ms. The binding is written after <c>config.toml</c>, so
/// if the session cannot be written the entries stay registered and a
/// second run, which reuses them, writes it.
/// </para>
/// </remarks>
public sealed class ServeMockCommandHandler
{
    /// <summary>The timeout of a device this command registers, as <c>visa add</c> defaults it.</summary>
    public const int DeviceTimeoutMilliseconds = 3000;

    /// <summary>The port a VXI-11 mock listens on without <c>--port</c>.</summary>
    public const int DefaultVxi11Port = 1024;

    private readonly IScenarioStore _scenarios;
    private readonly IConfigStore _config;
    private readonly ISessionStore _session;
    private readonly IServerProcessRegistry _registry;
    private readonly IProcessLiveness _liveness;
    private readonly IAuditLog _audit;
    private readonly IAuditSubject _subject;
    private readonly TimeProvider _time;

    /// <summary>
    /// Creates a new handler. <paramref name="audit"/> and
    /// <paramref name="subject"/> record one <see cref="ConfigMutated"/>,
    /// <c>mock.serve</c>, when a run adds to <c>config.toml</c> (ADR 0043).
    /// </summary>
    public ServeMockCommandHandler(
        IScenarioStore scenarios,
        IConfigStore config,
        ISessionStore session,
        IServerProcessRegistry registry,
        IProcessLiveness liveness,
        IAuditLog? audit = null,
        IAuditSubject? subject = null,
        TimeProvider? time = null
    )
    {
        _scenarios = scenarios;
        _config = config;
        _session = session;
        _registry = registry;
        _liveness = liveness;
        _audit = audit ?? NullAuditLog.Instance;
        _subject = subject ?? new StaticAuditSubject("unknown");
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Finds or registers the entries that serve the scenario.</summary>
    public async Task<Result<ServedMock, ServeMockError>> HandleAsync(
        ServeMockCommand command,
        CancellationToken ct
    )
    {
        switch (Parse(command))
        {
            case Result<Request, ServeMockError>.Error invalid:
                return Fail(invalid.Err);
            case Result<Request, ServeMockError>.Ok { Value: var request }:
                return await ServeAsync(request, ct);
            default:
                throw new InvalidOperationException("unknown Result variant");
        }
    }

    /// <summary>The validated request, with every default applied.</summary>
    private sealed record Request(
        ScenarioName Scenario,
        DeviceName DeviceName,
        Server Server,
        Route Route
    );

    private async Task<Result<ServedMock, ServeMockError>> ServeAsync(
        Request request,
        CancellationToken ct
    )
    {
        if (await CheckScenarioAsync(request.Scenario, ct) is { } scenarioError)
        {
            return Fail(scenarioError);
        }
        if (await CheckNotRunningAsync(request.Server.Name, ct) is { } runningError)
        {
            return Fail(runningError);
        }

        var configLoad = await _config.LoadAsync(ct);
        if (configLoad is Result<ConfigDocument, ConfigStoreError>.Error configErr)
        {
            return Fail(new ServeMockConfigFailure(configErr.Err));
        }
        var sessionLoad = await _session.LoadAsync(ct);
        if (sessionLoad is Result<SessionState, SessionStoreError>.Error sessionErr)
        {
            return Fail(new ServeMockSessionFailure(sessionErr.Err));
        }
        var config = configLoad.OrThrow();
        var session = sessionLoad.OrThrow();

        var device = config.FindDevice(request.DeviceName);
        if (device is not null && DeviceDifference(device, session, request.Scenario) is { } why)
        {
            return Fail(new ServeMockDeviceConflict(request.DeviceName, why));
        }
        var existingServer = config.FindServer(request.Server.Name);
        if (
            existingServer is not null
            && ServerDifference(existingServer, config, request) is { } diff
        )
        {
            return Fail(new ServeMockServerConflict(request.Server.Name, diff));
        }

        device ??= new Device(request.DeviceName, new VisaResource.Mock(), DeviceTimeout);
        var updated = Register(config, device, existingServer, request);
        if (!ReferenceEquals(updated, config))
        {
            if (
                await _config.SaveAsync(updated, ct) is Result<Unit, ConfigStoreError>.Error saveErr
            )
            {
                return Fail(new ServeMockConfigFailure(saveErr.Err));
            }
            await _audit.AppendAsync(
                new ConfigMutated(
                    _time.GetUtcNow(),
                    "mock.serve",
                    request.Server.Name.Value,
                    _subject.Get()
                ),
                ct
            );
        }

        if (session.GetActiveScenario(request.DeviceName) != request.Scenario)
        {
            var bound = session.BindScenario(request.DeviceName, request.Scenario);
            if (await _session.SaveAsync(bound, ct) is Result<Unit, SessionStoreError>.Error err)
            {
                return Fail(new ServeMockSessionFailure(err.Err));
            }
        }

        return Result.Success<ServedMock, ServeMockError>(
            new ServedMock(request.Scenario, device, request.Server, request.Route)
        );
    }

    /// <summary>
    /// <paramref name="config"/> with whichever of the device, the server and
    /// the route it lacks; the same instance when it lacks none.
    /// </summary>
    private static ConfigDocument Register(
        ConfigDocument config,
        Device device,
        Server? existingServer,
        Request request
    )
    {
        var updated = config;
        if (config.FindDevice(device.Name) is null)
        {
            updated = updated.AddDevice(device).OrThrow();
        }
        if (existingServer is null)
        {
            updated = updated.AddServer(request.Server).OrThrow();
        }
        if (updated.FindRoute(request.Route.ServerName, request.Route.Endpoint) is null)
        {
            updated = updated.AddRoute(request.Route).OrThrow();
        }
        return updated;
    }

    private async Task<ServeMockError?> CheckScenarioAsync(
        ScenarioName name,
        CancellationToken ct
    ) =>
        await _scenarios.ExistsAsync(name, ct) switch
        {
            Result<bool, ScenarioStoreError>.Ok { Value: true } => null,
            Result<bool, ScenarioStoreError>.Ok => new ServeMockScenarioNotFound(name),
            Result<bool, ScenarioStoreError>.Error err => new ServeMockScenarioStoreFailure(
                err.Err
            ),
            _ => throw new InvalidOperationException("unknown Result variant"),
        };

    private async Task<ServeMockError?> CheckNotRunningAsync(
        ServerName name,
        CancellationToken ct
    ) =>
        await _registry.ReadAsync(name, ct) switch
        {
            Result<ServerProcessEntry?, ServerProcessRegistryError>.Ok { Value: { } entry }
                when _liveness.IsAlive(entry.ProcessId) => new ServeMockServerRunning(
                name,
                entry.ProcessId
            ),
            Result<ServerProcessEntry?, ServerProcessRegistryError>.Ok => null,
            Result<ServerProcessEntry?, ServerProcessRegistryError>.Error err =>
                new ServeMockRegistryFailure(err.Err),
            _ => throw new InvalidOperationException("unknown Result variant"),
        };

    /// <summary>Why an existing device cannot serve the scenario; <see langword="null"/> when it can.</summary>
    private static string? DeviceDifference(
        Device device,
        SessionState session,
        ScenarioName scenario
    )
    {
        if (device.Resource is not VisaResource.Mock)
        {
            return $"its resource is {device.Resource.ToCanonical()}, not {VisaResource.Mock.Canonical}";
        }
        return session.GetActiveScenario(device.Name) is { } bound && bound != scenario
            ? $"it is bound to the scenario {bound.Value}"
            : null;
    }

    /// <summary>Why an existing server cannot serve the request; <see langword="null"/> when it can.</summary>
    private static string? ServerDifference(Server server, ConfigDocument config, Request request)
    {
        var wanted = request.Server;
        if (server.Type != wanted.Type)
        {
            return $"its type is {TypeName(server.Type)}, not {TypeName(wanted.Type)}";
        }
        if (server.Bind != wanted.Bind)
        {
            return $"its bind address is {server.Bind.Value}, not {wanted.Bind.Value}";
        }
        if (server.Port != wanted.Port)
        {
            return $"its port is {server.Port.Value}, not {wanted.Port.Value}";
        }
        foreach (var route in config.Routes.Where(r => r.ServerName == server.Name))
        {
            if (
                route.Endpoint != request.Route.Endpoint
                || route.DeviceName != request.Route.DeviceName
            )
            {
                return $"it has the route {route.Endpoint.Value} -> {route.DeviceName.Value}";
            }
            if (wanted.Type == ServerType.UsbIp && route.Profile != request.Route.Profile)
            {
                return $"its route {route.Endpoint.Value} has the profile {ProfileName(route.Profile)}, not {ProfileName(request.Route.Profile)}";
            }
        }
        return null;
    }

    private static Result<Request, ServeMockError> Parse(ServeMockCommand command)
    {
        if (
            ScenarioName.From(command.Scenario)
            is not Result<ScenarioName, ScenarioNameError>.Ok { Value: var scenario }
        )
        {
            return Invalid(new ServeMockInvalidScenario(command.Scenario));
        }
        if (ParseType(command.Type) is not { } type)
        {
            return Invalid(new ServeMockInvalidType(command.Type));
        }
        if (ParseProfile(command.Profile) is not { } profile)
        {
            return Invalid(new ServeMockInvalidProfile(command.Profile!));
        }
        var portNumber = command.Port ?? DefaultPort(type);
        if (Port.From(portNumber) is not Result<Port, PortError>.Ok { Value: var port })
        {
            return Invalid(new ServeMockInvalidPort(portNumber));
        }
        if (
            IpAddress.From(command.Bind)
            is not Result<IpAddress, IpAddressError>.Ok { Value: var bind }
        )
        {
            return Invalid(new ServeMockInvalidBind(command.Bind));
        }

        var alias = command.Alias ?? $"mock-{TypeName(type)}-{port.Value}";
        if (
            DeviceName.From(alias)
                is not Result<DeviceName, DeviceError>.Ok { Value: var deviceName }
            || ServerName.From(alias)
                is not Result<ServerName, ServerNameError>.Ok { Value: var serverName }
        )
        {
            return Invalid(new ServeMockInvalidAlias(alias));
        }
        if (serverName == ServerName.ManagementApi)
        {
            return Invalid(new ServeMockReservedName(serverName));
        }

        var endpoint = PublicEndpoint.From(Endpoint(type, port)).OrThrow();
        var route = new Route(serverName, endpoint, deviceName)
        {
            Profile = type == ServerType.UsbIp ? profile : UsbExportProfile.UsbTmc,
        };
        return Result.Success<Request, ServeMockError>(
            new Request(scenario, deviceName, new Server(serverName, type, bind, port), route)
        );
    }

    private static ServerType? ParseType(string raw) =>
        raw.ToLowerInvariant() switch
        {
            "socket" => ServerType.Socket,
            "hislip" => ServerType.HiSlip,
            "vxi11" => ServerType.Vxi11,
            "usbip" => ServerType.UsbIp,
            _ => null,
        };

    private static UsbExportProfile? ParseProfile(string? raw) =>
        raw?.ToLowerInvariant() switch
        {
            null or "usbtmc" => UsbExportProfile.UsbTmc,
            "cdc-acm" => UsbExportProfile.CdcAcm,
            _ => null,
        };

    private static int DefaultPort(ServerType type) =>
        type switch
        {
            ServerType.Socket => 5025,
            ServerType.HiSlip => 4880,
            ServerType.UsbIp => UsbIpConstants.DefaultPort,
            _ => DefaultVxi11Port,
        };

    /// <summary>The route's endpoint, fixed by the type (ADR 0056 §2).</summary>
    private static string Endpoint(ServerType type, Port port) =>
        type switch
        {
            ServerType.Socket => port.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ),
            ServerType.HiSlip => "hislip0",
            ServerType.UsbIp => "1-1",
            _ => "inst0",
        };

    private static string TypeName(ServerType type) => type.ToString().ToLowerInvariant();

    private static string ProfileName(UsbExportProfile profile) =>
        profile == UsbExportProfile.CdcAcm ? "cdc-acm" : "usbtmc";

    private static readonly Timeout DeviceTimeout = Timeout
        .FromMilliseconds(DeviceTimeoutMilliseconds)
        .OrThrow();

    private static Result<Request, ServeMockError> Invalid(ServeMockError error) =>
        Result.Failure<Request, ServeMockError>(error);

    private static Result<ServedMock, ServeMockError> Fail(ServeMockError error) =>
        Result.Failure<ServedMock, ServeMockError>(error);
}

/// <summary>Unwraps a result this handler has already ruled the failure of out.</summary>
file static class ResultExtensions
{
    public static T OrThrow<T, TError>(this Result<T, TError> result) =>
        result is Result<T, TError>.Ok ok
            ? ok.Value
            : throw new InvalidOperationException("a result ruled out as a failure failed");
}
