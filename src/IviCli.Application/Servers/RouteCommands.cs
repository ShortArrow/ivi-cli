using System.Collections.Immutable;
using IviCli.Application.Audit;
using IviCli.Application.Configuration;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Servers;

namespace IviCli.Application.Servers;

// ===== DTOs =====================================================

/// <summary>Command DTO for adding a route.</summary>
/// <param name="Server">The owning gateway server's alias.</param>
/// <param name="Endpoint">The public endpoint name.</param>
/// <param name="Device">The device alias to bind.</param>
/// <param name="Profile">
/// The USB profile a USB/IP export presents — <c>usbtmc</c> or
/// <c>cdc-acm</c> (ADR 0049 §5). Null leaves the route on the default,
/// which is what every route on every other server type stays on.
/// </param>
public sealed record AddRouteCommand(
    string Server,
    string Endpoint,
    string Device,
    string? Profile = null
);

/// <summary>Command DTO for removing a route.</summary>
public sealed record RemoveRouteCommand(string Server, string Endpoint);

/// <summary>Query DTO for listing routes.</summary>
public sealed record ListRoutesQuery;

/// <summary>The listing result.</summary>
public sealed record RouteListing(ImmutableArray<Route> Routes);

// ===== Errors ====================================================

/// <summary>Errors for the add-route command.</summary>
public abstract record AddRouteError : IviError
{
    public abstract LogSeverity Severity { get; }
    public abstract string Message { get; }
    public virtual IReadOnlyList<object?> LogArgs => Array.Empty<object?>();
    public virtual Exception? Cause => null;
}

/// <summary>The server name failed validation.</summary>
public sealed record AddRouteInvalidServer(string Raw) : AddRouteError
{
    public override LogSeverity Severity => LogSeverity.Warning;
    public override string Message => "invalid server name: {Raw}";
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The endpoint failed validation.</summary>
public sealed record AddRouteInvalidEndpoint(string Raw) : AddRouteError
{
    public override LogSeverity Severity => LogSeverity.Warning;
    public override string Message => "invalid public endpoint: {Raw}";
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The device name failed validation.</summary>
public sealed record AddRouteInvalidDevice(string Raw) : AddRouteError
{
    public override LogSeverity Severity => LogSeverity.Warning;
    public override string Message => "invalid device name: {Raw}";
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The USB export profile is not one this build exports.</summary>
public sealed record AddRouteInvalidProfile(string Raw) : AddRouteError
{
    public override LogSeverity Severity => LogSeverity.Warning;
    public override string Message => "unknown USB export profile: {Raw}";
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The route's server does not exist.</summary>
public sealed record AddRouteServerMissing(ServerName Server) : AddRouteError
{
    public override LogSeverity Severity => LogSeverity.Warning;
    public override string Message => "no such server: {Server}";
    public override IReadOnlyList<object?> LogArgs => new object?[] { Server };
}

/// <summary>The route's device does not exist.</summary>
public sealed record AddRouteDeviceMissing(DeviceName Device) : AddRouteError
{
    public override LogSeverity Severity => LogSeverity.Warning;
    public override string Message => "no such device: {Device}";
    public override IReadOnlyList<object?> LogArgs => new object?[] { Device };
}

/// <summary>The (server, endpoint) pair is already in use.</summary>
public sealed record AddRouteDuplicate(ServerName Server, PublicEndpoint Endpoint) : AddRouteError
{
    public override LogSeverity Severity => LogSeverity.Warning;
    public override string Message => "route already exists: {Server}/{Endpoint}";
    public override IReadOnlyList<object?> LogArgs => new object?[] { Server, Endpoint };
}

/// <summary>Storage failure during add-route.</summary>
public sealed record AddRouteStoreFailure(ConfigStoreError Inner) : AddRouteError
{
    public override LogSeverity Severity => Inner.Severity;
    public override string Message => Inner.Message;
    public override IReadOnlyList<object?> LogArgs => Inner.LogArgs;
    public override Exception? Cause => Inner.Cause;
}

/// <summary>Errors for the remove-route command.</summary>
public abstract record RemoveRouteError : IviError
{
    public abstract LogSeverity Severity { get; }
    public abstract string Message { get; }
    public virtual IReadOnlyList<object?> LogArgs => Array.Empty<object?>();
    public virtual Exception? Cause => null;
}

/// <summary>Invalid server name on remove.</summary>
public sealed record RemoveRouteInvalidServer(string Raw) : RemoveRouteError
{
    public override LogSeverity Severity => LogSeverity.Warning;
    public override string Message => "invalid server name: {Raw}";
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>Invalid endpoint on remove.</summary>
public sealed record RemoveRouteInvalidEndpoint(string Raw) : RemoveRouteError
{
    public override LogSeverity Severity => LogSeverity.Warning;
    public override string Message => "invalid public endpoint: {Raw}";
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The route doesn't exist.</summary>
public sealed record RemoveRouteNotFound(ServerName Server, PublicEndpoint Endpoint)
    : RemoveRouteError
{
    public override LogSeverity Severity => LogSeverity.Warning;
    public override string Message => "no such route: {Server}/{Endpoint}";
    public override IReadOnlyList<object?> LogArgs => new object?[] { Server, Endpoint };
}

/// <summary>Storage failure during remove-route.</summary>
public sealed record RemoveRouteStoreFailure(ConfigStoreError Inner) : RemoveRouteError
{
    public override LogSeverity Severity => Inner.Severity;
    public override string Message => Inner.Message;
    public override IReadOnlyList<object?> LogArgs => Inner.LogArgs;
    public override Exception? Cause => Inner.Cause;
}

/// <summary>Errors for list-routes.</summary>
public abstract record ListRoutesError : IviError
{
    public abstract LogSeverity Severity { get; }
    public abstract string Message { get; }
    public virtual IReadOnlyList<object?> LogArgs => Array.Empty<object?>();
    public virtual Exception? Cause => null;
}

/// <summary>Storage failure during list-routes.</summary>
public sealed record ListRoutesStoreFailure(ConfigStoreError Inner) : ListRoutesError
{
    public override LogSeverity Severity => Inner.Severity;
    public override string Message => Inner.Message;
    public override IReadOnlyList<object?> LogArgs => Inner.LogArgs;
    public override Exception? Cause => Inner.Cause;
}

// ===== Handlers ==================================================

/// <summary>Adds a route to the configuration.</summary>
public sealed class AddRouteCommandHandler
{
    private readonly IConfigStore _store;
    private readonly IAuditLog _audit;
    private readonly IAuditSubject _subject;
    private readonly TimeProvider _time;

    /// <summary>Creates a new handler.</summary>
    public AddRouteCommandHandler(
        IConfigStore store,
        IAuditLog? audit = null,
        IAuditSubject? subject = null,
        TimeProvider? time = null
    )
    {
        _store = store;
        _audit = audit ?? NullAuditLog.Instance;
        _subject = subject ?? new StaticAuditSubject("unknown");
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Validates, parses, and persists the new route.</summary>
    public async Task<Result<Route, AddRouteError>> HandleAsync(
        AddRouteCommand command,
        CancellationToken ct
    )
    {
        if (
            ServerName.From(command.Server)
            is not Result<ServerName, ServerNameError>.Ok { Value: var serverName }
        )
        {
            return Result.Failure<Route, AddRouteError>(new AddRouteInvalidServer(command.Server));
        }
        if (
            PublicEndpoint.From(command.Endpoint)
            is not Result<PublicEndpoint, PublicEndpointError>.Ok { Value: var endpoint }
        )
        {
            return Result.Failure<Route, AddRouteError>(
                new AddRouteInvalidEndpoint(command.Endpoint)
            );
        }
        if (
            DeviceName.From(command.Device)
            is not Result<DeviceName, DeviceError>.Ok { Value: var deviceName }
        )
        {
            return Result.Failure<Route, AddRouteError>(new AddRouteInvalidDevice(command.Device));
        }
        var profile = command.Profile?.ToLowerInvariant() switch
        {
            null => UsbExportProfile.UsbTmc,
            "usbtmc" => UsbExportProfile.UsbTmc,
            "cdc-acm" => (UsbExportProfile?)UsbExportProfile.CdcAcm,
            _ => null,
        };
        if (profile is null)
        {
            return Result.Failure<Route, AddRouteError>(
                new AddRouteInvalidProfile(command.Profile!)
            );
        }

        var loadResult = await _store.LoadAsync(ct);
        if (loadResult is not Result<ConfigDocument, ConfigStoreError>.Ok { Value: var config })
        {
            var err = ((Result<ConfigDocument, ConfigStoreError>.Error)loadResult).Err;
            return Result.Failure<Route, AddRouteError>(new AddRouteStoreFailure(err));
        }

        var route = new Route(serverName, endpoint, deviceName) { Profile = profile.Value };
        var addResult = config.AddRoute(route);
        if (addResult is not Result<ConfigDocument, ConfigError>.Ok { Value: var updated })
        {
            var addErr = ((Result<ConfigDocument, ConfigError>.Error)addResult).Err;
            return Result.Failure<Route, AddRouteError>(
                addErr switch
                {
                    RouteServerMissing rsm => new AddRouteServerMissing(rsm.Server),
                    RouteDeviceMissing rdm => new AddRouteDeviceMissing(rdm.Device),
                    DuplicateRoute dr => new AddRouteDuplicate(dr.Server, dr.Endpoint),
                    _ => (AddRouteError)
                        new AddRouteStoreFailure(new ConfigStoreParseFailure(addErr.Message)),
                }
            );
        }

        var saveResult = await _store.SaveAsync(updated, ct);
        if (saveResult is not Result<Unit, ConfigStoreError>.Ok)
        {
            var err = ((Result<Unit, ConfigStoreError>.Error)saveResult).Err;
            return Result.Failure<Route, AddRouteError>(new AddRouteStoreFailure(err));
        }

        await _audit.AppendAsync(
            new ConfigMutated(
                _time.GetUtcNow(),
                "route.add",
                $"{serverName.Value}/{endpoint.Value}",
                _subject.Get()
            ),
            ct
        );

        return Result.Success<Route, AddRouteError>(route);
    }
}

/// <summary>Removes a route by (server, endpoint).</summary>
public sealed class RemoveRouteCommandHandler
{
    private readonly IConfigStore _store;
    private readonly IAuditLog _audit;
    private readonly IAuditSubject _subject;
    private readonly TimeProvider _time;

    /// <summary>Creates a new handler.</summary>
    public RemoveRouteCommandHandler(
        IConfigStore store,
        IAuditLog? audit = null,
        IAuditSubject? subject = null,
        TimeProvider? time = null
    )
    {
        _store = store;
        _audit = audit ?? NullAuditLog.Instance;
        _subject = subject ?? new StaticAuditSubject("unknown");
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Persists the removal.</summary>
    public async Task<Result<Unit, RemoveRouteError>> HandleAsync(
        RemoveRouteCommand command,
        CancellationToken ct
    )
    {
        if (
            ServerName.From(command.Server)
            is not Result<ServerName, ServerNameError>.Ok { Value: var serverName }
        )
        {
            return Result.Failure<Unit, RemoveRouteError>(
                new RemoveRouteInvalidServer(command.Server)
            );
        }
        if (
            PublicEndpoint.From(command.Endpoint)
            is not Result<PublicEndpoint, PublicEndpointError>.Ok { Value: var endpoint }
        )
        {
            return Result.Failure<Unit, RemoveRouteError>(
                new RemoveRouteInvalidEndpoint(command.Endpoint)
            );
        }

        var loadResult = await _store.LoadAsync(ct);
        if (loadResult is not Result<ConfigDocument, ConfigStoreError>.Ok { Value: var config })
        {
            var err = ((Result<ConfigDocument, ConfigStoreError>.Error)loadResult).Err;
            return Result.Failure<Unit, RemoveRouteError>(new RemoveRouteStoreFailure(err));
        }

        var removeResult = config.RemoveRoute(serverName, endpoint);
        if (removeResult is not Result<ConfigDocument, ConfigError>.Ok { Value: var updated })
        {
            return Result.Failure<Unit, RemoveRouteError>(
                new RemoveRouteNotFound(serverName, endpoint)
            );
        }

        var saveResult = await _store.SaveAsync(updated, ct);
        if (saveResult is not Result<Unit, ConfigStoreError>.Ok)
        {
            var err = ((Result<Unit, ConfigStoreError>.Error)saveResult).Err;
            return Result.Failure<Unit, RemoveRouteError>(new RemoveRouteStoreFailure(err));
        }

        await _audit.AppendAsync(
            new ConfigMutated(
                _time.GetUtcNow(),
                "route.remove",
                $"{serverName.Value}/{endpoint.Value}",
                _subject.Get()
            ),
            ct
        );

        return Result.Success<Unit, RemoveRouteError>(Unit.Value);
    }
}

/// <summary>Lists configured routes.</summary>
public sealed class ListRoutesQueryHandler
{
    private readonly IConfigStore _store;

    /// <summary>Creates a new handler.</summary>
    public ListRoutesQueryHandler(IConfigStore store) => _store = store;

    /// <summary>Loads the config and projects routes.</summary>
    public async Task<Result<RouteListing, ListRoutesError>> HandleAsync(
        ListRoutesQuery query,
        CancellationToken ct
    )
    {
        var loadResult = await _store.LoadAsync(ct);
        return loadResult switch
        {
            Result<ConfigDocument, ConfigStoreError>.Ok ok => Result.Success<
                RouteListing,
                ListRoutesError
            >(new RouteListing(ok.Value.Routes)),
            Result<ConfigDocument, ConfigStoreError>.Error err => Result.Failure<
                RouteListing,
                ListRoutesError
            >(new ListRoutesStoreFailure(err.Err)),
            _ => throw new InvalidOperationException("unknown Result variant"),
        };
    }
}
