using System.Collections.Immutable;
using IviCli.Domain.Devices;
using IviCli.Domain.Servers;

namespace IviCli.Domain.Configuration;

/// <summary>
/// The structured, validated form of <c>config.toml</c>. Holds devices,
/// servers, routes, and the project-wide defaults.
/// </summary>
/// <remarks>
/// All mutators return a new <see cref="ConfigDocument"/> (ADR 0023
/// immutability). Cross-entity invariants — name uniqueness, route
/// targets existing — are enforced by the mutators themselves, so any
/// reachable <see cref="ConfigDocument"/> is known self-consistent.
/// </remarks>
public sealed record ConfigDocument
{
    /// <summary>An empty configuration.</summary>
    public static ConfigDocument Empty { get; } =
        new(
            devices: ImmutableArray<Device>.Empty,
            servers: ImmutableArray<Server>.Empty,
            routes: ImmutableArray<Route>.Empty,
            defaults: Defaults.None,
            pool: PoolConfig.Default,
            api: ApiConfig.Default,
            telemetry: TelemetryConfig.Default,
            audit: AuditConfig.Default
        );

    /// <summary>The configured devices, in insertion order.</summary>
    public ImmutableArray<Device> Devices { get; }

    /// <summary>The configured gateway servers, in insertion order.</summary>
    public ImmutableArray<Server> Servers { get; }

    /// <summary>The configured server-to-device routes.</summary>
    public ImmutableArray<Route> Routes { get; }

    /// <summary>The <c>[defaults]</c> section.</summary>
    public Defaults Defaults { get; }

    /// <summary>The <c>[pool]</c> section (ADR 0038).</summary>
    public PoolConfig Pool { get; }

    /// <summary>The <c>[api]</c> section (ADR 0039).</summary>
    public ApiConfig Api { get; }

    /// <summary>The <c>[telemetry]</c> section (ADR 0040).</summary>
    public TelemetryConfig Telemetry { get; }

    /// <summary>The <c>[audit]</c> section (ADR 0043).</summary>
    public AuditConfig Audit { get; }

    private ConfigDocument(
        ImmutableArray<Device> devices,
        ImmutableArray<Server> servers,
        ImmutableArray<Route> routes,
        Defaults defaults,
        PoolConfig pool,
        ApiConfig api,
        TelemetryConfig telemetry,
        AuditConfig audit
    )
    {
        Devices = devices;
        Servers = servers;
        Routes = routes;
        Defaults = defaults;
        Pool = pool;
        Api = api;
        Telemetry = telemetry;
        Audit = audit;
    }

    /// <summary>Structural equality across every collection.</summary>
    public bool Equals(ConfigDocument? other) =>
        other is not null
        && Defaults == other.Defaults
        && Pool == other.Pool
        && Api == other.Api
        && Telemetry == other.Telemetry
        && Audit == other.Audit
        && Devices.SequenceEqual(other.Devices)
        && Servers.SequenceEqual(other.Servers)
        && Routes.SequenceEqual(other.Routes);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Defaults);
        hash.Add(Pool);
        hash.Add(Api);
        hash.Add(Telemetry);
        hash.Add(Audit);
        foreach (var d in Devices)
        {
            hash.Add(d);
        }
        foreach (var s in Servers)
        {
            hash.Add(s);
        }
        foreach (var r in Routes)
        {
            hash.Add(r);
        }
        return hash.ToHashCode();
    }

    /// <summary>Replaces the <see cref="Pool"/> section.</summary>
    public ConfigDocument WithPool(PoolConfig pool) => With(pool: pool);

    /// <summary>Replaces the <see cref="Api"/> section.</summary>
    public ConfigDocument WithApi(ApiConfig api) => With(api: api);

    /// <summary>Replaces the <see cref="Telemetry"/> section.</summary>
    public ConfigDocument WithTelemetry(TelemetryConfig telemetry) => With(telemetry: telemetry);

    /// <summary>Replaces the <see cref="Audit"/> section.</summary>
    public ConfigDocument WithAudit(AuditConfig audit) => With(audit: audit);

    // -------- Devices ----------------------------------------------------

    /// <summary>Finds a device by alias, or returns <see langword="null"/>.</summary>
    public Device? FindDevice(DeviceName name)
    {
        foreach (var device in Devices)
        {
            if (device.Name == name)
            {
                return device;
            }
        }
        return null;
    }

    /// <summary>Appends a device. Fails on duplicate name.</summary>
    public Result<ConfigDocument, ConfigError> AddDevice(Device device)
    {
        if (FindDevice(device.Name) is not null)
        {
            return Result.Failure<ConfigDocument, ConfigError>(
                new DuplicateDeviceName(device.Name)
            );
        }
        return Result.Success<ConfigDocument, ConfigError>(With(devices: Devices.Add(device)));
    }

    /// <summary>
    /// Removes a device. Cascades through Routes (any route pointing at the
    /// removed device is also removed) and clears the default if it pointed
    /// at the removed device.
    /// </summary>
    public Result<ConfigDocument, ConfigError> RemoveDevice(DeviceName name)
    {
        var existing = FindDevice(name);
        if (existing is null)
        {
            return Result.Failure<ConfigDocument, ConfigError>(new DeviceNotFound(name));
        }

        var nextDevices = Devices.Remove(existing);
        var nextRoutes = Routes.RemoveAll(r => r.DeviceName == name);
        var nextDefaults = Defaults.Device == name ? Defaults with { Device = null } : Defaults;
        return Result.Success<ConfigDocument, ConfigError>(
            With(devices: nextDevices, routes: nextRoutes, defaults: nextDefaults)
        );
    }

    /// <summary>Sets or clears the default device.</summary>
    public Result<ConfigDocument, ConfigError> SetDefaultDevice(DeviceName? name)
    {
        if (name is not null && FindDevice(name) is null)
        {
            return Result.Failure<ConfigDocument, ConfigError>(new DefaultDeviceMissing(name));
        }
        return Result.Success<ConfigDocument, ConfigError>(
            With(defaults: Defaults with { Device = name })
        );
    }

    // -------- Servers ----------------------------------------------------

    /// <summary>Finds a server by name.</summary>
    public Server? FindServer(ServerName name)
    {
        foreach (var s in Servers)
        {
            if (s.Name == name)
            {
                return s;
            }
        }
        return null;
    }

    /// <summary>Appends a server. Fails on duplicate name.</summary>
    public Result<ConfigDocument, ConfigError> AddServer(Server server)
    {
        if (FindServer(server.Name) is not null)
        {
            return Result.Failure<ConfigDocument, ConfigError>(
                new DuplicateServerName(server.Name)
            );
        }
        return Result.Success<ConfigDocument, ConfigError>(With(servers: Servers.Add(server)));
    }

    /// <summary>
    /// Removes a server. Cascades through Routes (any route on the removed
    /// server is also removed).
    /// </summary>
    public Result<ConfigDocument, ConfigError> RemoveServer(ServerName name)
    {
        var existing = FindServer(name);
        if (existing is null)
        {
            return Result.Failure<ConfigDocument, ConfigError>(new ServerNotFound(name));
        }
        var nextServers = Servers.Remove(existing);
        var nextRoutes = Routes.RemoveAll(r => r.ServerName == name);
        return Result.Success<ConfigDocument, ConfigError>(
            With(servers: nextServers, routes: nextRoutes)
        );
    }

    // -------- Routes -----------------------------------------------------

    /// <summary>Finds a route by (server, endpoint) pair.</summary>
    public Route? FindRoute(ServerName server, PublicEndpoint endpoint)
    {
        foreach (var r in Routes)
        {
            if (r.ServerName == server && r.Endpoint == endpoint)
            {
                return r;
            }
        }
        return null;
    }

    /// <summary>
    /// Appends a route. Fails if the route's server or device is missing,
    /// or if the (server, endpoint) pair is already used.
    /// </summary>
    public Result<ConfigDocument, ConfigError> AddRoute(Route route)
    {
        if (FindServer(route.ServerName) is null)
        {
            return Result.Failure<ConfigDocument, ConfigError>(
                new RouteServerMissing(route.ServerName)
            );
        }
        if (FindDevice(route.DeviceName) is null)
        {
            return Result.Failure<ConfigDocument, ConfigError>(
                new RouteDeviceMissing(route.DeviceName)
            );
        }
        if (FindRoute(route.ServerName, route.Endpoint) is not null)
        {
            return Result.Failure<ConfigDocument, ConfigError>(
                new DuplicateRoute(route.ServerName, route.Endpoint)
            );
        }
        return Result.Success<ConfigDocument, ConfigError>(With(routes: Routes.Add(route)));
    }

    /// <summary>Removes a route by (server, endpoint).</summary>
    public Result<ConfigDocument, ConfigError> RemoveRoute(
        ServerName server,
        PublicEndpoint endpoint
    )
    {
        var existing = FindRoute(server, endpoint);
        if (existing is null)
        {
            return Result.Failure<ConfigDocument, ConfigError>(new RouteNotFound(server, endpoint));
        }
        return Result.Success<ConfigDocument, ConfigError>(With(routes: Routes.Remove(existing)));
    }

    private ConfigDocument With(
        ImmutableArray<Device>? devices = null,
        ImmutableArray<Server>? servers = null,
        ImmutableArray<Route>? routes = null,
        Defaults? defaults = null,
        PoolConfig? pool = null,
        ApiConfig? api = null,
        TelemetryConfig? telemetry = null,
        AuditConfig? audit = null
    ) =>
        new(
            devices ?? Devices,
            servers ?? Servers,
            routes ?? Routes,
            defaults ?? Defaults,
            pool ?? Pool,
            api ?? Api,
            telemetry ?? Telemetry,
            audit ?? Audit
        );
}
