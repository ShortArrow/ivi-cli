using System.Collections.Immutable;
using IviCli.Application.Audit;
using IviCli.Application.Configuration;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Servers;

namespace IviCli.Application.Servers;

// ===== DTOs =====================================================

/// <summary>Command DTO for adding a server.</summary>
public sealed record AddServerCommand(string Name, string Type, string Bind, int Port);

/// <summary>Command DTO for removing a server.</summary>
public sealed record RemoveServerCommand(string Name);

/// <summary>Query DTO for listing servers.</summary>
public sealed record ListServersQuery;

/// <summary>The listing result.</summary>
public sealed record ServerListing(ImmutableArray<Server> Servers);

// ===== Errors ====================================================

/// <summary>Errors that the add-server command can fail with.</summary>
public abstract record AddServerError : IviError
{
    /// <inheritdoc/>
    public abstract LogSeverity Severity { get; }

    /// <inheritdoc/>
    public abstract string Message { get; }

    /// <inheritdoc/>
    public virtual IReadOnlyList<object?> LogArgs => Array.Empty<object?>();

    /// <inheritdoc/>
    public virtual Exception? Cause => null;
}

/// <summary>The server name failed validation.</summary>
public sealed record AddServerInvalidName(string Raw) : AddServerError
{
    public override LogSeverity Severity => LogSeverity.Warning;
    public override string Message => "invalid server name: {Raw}";
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The supplied type is not one of local / socket / hislip / vxi11.</summary>
public sealed record AddServerInvalidType(string Raw) : AddServerError
{
    public override LogSeverity Severity => LogSeverity.Warning;
    public override string Message => "invalid server type: {Raw}";
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The bind address failed validation.</summary>
public sealed record AddServerInvalidBind(string Raw) : AddServerError
{
    public override LogSeverity Severity => LogSeverity.Warning;
    public override string Message => "invalid bind: {Raw}";
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The port is out of range.</summary>
public sealed record AddServerInvalidPort(int Raw) : AddServerError
{
    public override LogSeverity Severity => LogSeverity.Warning;
    public override string Message => "invalid port: {Raw}";
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>A server with the same name already exists.</summary>
public sealed record AddServerDuplicate(ServerName Name) : AddServerError
{
    public override LogSeverity Severity => LogSeverity.Warning;
    public override string Message => "server already exists: {Name}";
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>The config store could not be read or written.</summary>
public sealed record AddServerStoreFailure(ConfigStoreError Inner) : AddServerError
{
    public override LogSeverity Severity => Inner.Severity;
    public override string Message => Inner.Message;
    public override IReadOnlyList<object?> LogArgs => Inner.LogArgs;
    public override Exception? Cause => Inner.Cause;
}

/// <summary>Errors for remove-server.</summary>
public abstract record RemoveServerError : IviError
{
    public abstract LogSeverity Severity { get; }
    public abstract string Message { get; }
    public virtual IReadOnlyList<object?> LogArgs => Array.Empty<object?>();
    public virtual Exception? Cause => null;
}

/// <summary>Invalid server name on remove.</summary>
public sealed record RemoveServerInvalidName(string Raw) : RemoveServerError
{
    public override LogSeverity Severity => LogSeverity.Warning;
    public override string Message => "invalid server name: {Raw}";
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The server does not exist.</summary>
public sealed record RemoveServerNotFound(ServerName Name) : RemoveServerError
{
    public override LogSeverity Severity => LogSeverity.Warning;
    public override string Message => "server not found: {Name}";
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>Storage failure during remove.</summary>
public sealed record RemoveServerStoreFailure(ConfigStoreError Inner) : RemoveServerError
{
    public override LogSeverity Severity => Inner.Severity;
    public override string Message => Inner.Message;
    public override IReadOnlyList<object?> LogArgs => Inner.LogArgs;
    public override Exception? Cause => Inner.Cause;
}

/// <summary>Errors for list-servers.</summary>
public abstract record ListServersError : IviError
{
    public abstract LogSeverity Severity { get; }
    public abstract string Message { get; }
    public virtual IReadOnlyList<object?> LogArgs => Array.Empty<object?>();
    public virtual Exception? Cause => null;
}

/// <summary>Storage failure during list.</summary>
public sealed record ListServersStoreFailure(ConfigStoreError Inner) : ListServersError
{
    public override LogSeverity Severity => Inner.Severity;
    public override string Message => Inner.Message;
    public override IReadOnlyList<object?> LogArgs => Inner.LogArgs;
    public override Exception? Cause => Inner.Cause;
}

// ===== Handlers ==================================================

/// <summary>Adds a configured server.</summary>
public sealed class AddServerCommandHandler
{
    private readonly IConfigStore _store;
    private readonly IAuditLog _audit;
    private readonly IAuditSubject _subject;
    private readonly TimeProvider _time;

    /// <summary>Creates a new handler.</summary>
    public AddServerCommandHandler(
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

    /// <summary>Validates, parses, and persists the new server.</summary>
    public async Task<Result<ServerName, AddServerError>> HandleAsync(
        AddServerCommand command,
        CancellationToken ct
    )
    {
        if (
            ServerName.From(command.Name)
            is not Result<ServerName, ServerNameError>.Ok { Value: var name }
        )
        {
            return Result.Failure<ServerName, AddServerError>(
                new AddServerInvalidName(command.Name)
            );
        }

        var type = command.Type.ToLowerInvariant() switch
        {
            "local" => (ServerType?)ServerType.Local,
            "socket" => ServerType.Socket,
            "hislip" => ServerType.HiSlip,
            "vxi11" => ServerType.Vxi11,
            "usbip" => ServerType.UsbIp,
            _ => null,
        };
        if (type is null)
        {
            return Result.Failure<ServerName, AddServerError>(
                new AddServerInvalidType(command.Type)
            );
        }

        if (
            IpAddress.From(command.Bind)
            is not Result<IpAddress, IpAddressError>.Ok { Value: var bind }
        )
        {
            return Result.Failure<ServerName, AddServerError>(
                new AddServerInvalidBind(command.Bind)
            );
        }

        if (Port.From(command.Port) is not Result<Port, PortError>.Ok { Value: var port })
        {
            return Result.Failure<ServerName, AddServerError>(
                new AddServerInvalidPort(command.Port)
            );
        }

        var loadResult = await _store.LoadAsync(ct);
        if (loadResult is not Result<ConfigDocument, ConfigStoreError>.Ok { Value: var config })
        {
            var err = ((Result<ConfigDocument, ConfigStoreError>.Error)loadResult).Err;
            return Result.Failure<ServerName, AddServerError>(new AddServerStoreFailure(err));
        }

        var server = new Server(name, type.Value, bind, port);
        var addResult = config.AddServer(server);
        if (addResult is not Result<ConfigDocument, ConfigError>.Ok { Value: var updated })
        {
            var addErr = ((Result<ConfigDocument, ConfigError>.Error)addResult).Err;
            if (addErr is DuplicateServerName)
            {
                return Result.Failure<ServerName, AddServerError>(new AddServerDuplicate(name));
            }
            return Result.Failure<ServerName, AddServerError>(
                new AddServerStoreFailure(new ConfigStoreParseFailure(addErr.Message))
            );
        }

        var saveResult = await _store.SaveAsync(updated, ct);
        if (saveResult is not Result<Unit, ConfigStoreError>.Ok)
        {
            var err = ((Result<Unit, ConfigStoreError>.Error)saveResult).Err;
            return Result.Failure<ServerName, AddServerError>(new AddServerStoreFailure(err));
        }

        await _audit.AppendAsync(
            new ConfigMutated(_time.GetUtcNow(), "server.add", name.Value, _subject.Get()),
            ct
        );

        return Result.Success<ServerName, AddServerError>(name);
    }
}

/// <summary>Removes a configured server (cascades through Routes).</summary>
public sealed class RemoveServerCommandHandler
{
    private readonly IConfigStore _store;
    private readonly IAuditLog _audit;
    private readonly IAuditSubject _subject;
    private readonly TimeProvider _time;

    /// <summary>Creates a new handler.</summary>
    public RemoveServerCommandHandler(
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

    /// <summary>Validates and persists the removal.</summary>
    public async Task<Result<ServerName, RemoveServerError>> HandleAsync(
        RemoveServerCommand command,
        CancellationToken ct
    )
    {
        if (
            ServerName.From(command.Name)
            is not Result<ServerName, ServerNameError>.Ok { Value: var name }
        )
        {
            return Result.Failure<ServerName, RemoveServerError>(
                new RemoveServerInvalidName(command.Name)
            );
        }

        var loadResult = await _store.LoadAsync(ct);
        if (loadResult is not Result<ConfigDocument, ConfigStoreError>.Ok { Value: var config })
        {
            var err = ((Result<ConfigDocument, ConfigStoreError>.Error)loadResult).Err;
            return Result.Failure<ServerName, RemoveServerError>(new RemoveServerStoreFailure(err));
        }

        var removeResult = config.RemoveServer(name);
        if (removeResult is not Result<ConfigDocument, ConfigError>.Ok { Value: var updated })
        {
            return Result.Failure<ServerName, RemoveServerError>(new RemoveServerNotFound(name));
        }

        var saveResult = await _store.SaveAsync(updated, ct);
        if (saveResult is not Result<Unit, ConfigStoreError>.Ok)
        {
            var err = ((Result<Unit, ConfigStoreError>.Error)saveResult).Err;
            return Result.Failure<ServerName, RemoveServerError>(new RemoveServerStoreFailure(err));
        }

        await _audit.AppendAsync(
            new ConfigMutated(_time.GetUtcNow(), "server.remove", name.Value, _subject.Get()),
            ct
        );

        return Result.Success<ServerName, RemoveServerError>(name);
    }
}

/// <summary>Lists configured servers.</summary>
public sealed class ListServersQueryHandler
{
    private readonly IConfigStore _store;

    /// <summary>Creates a new handler.</summary>
    public ListServersQueryHandler(IConfigStore store) => _store = store;

    /// <summary>Loads the config and projects servers.</summary>
    public async Task<Result<ServerListing, ListServersError>> HandleAsync(
        ListServersQuery query,
        CancellationToken ct
    )
    {
        var loadResult = await _store.LoadAsync(ct);
        return loadResult switch
        {
            Result<ConfigDocument, ConfigStoreError>.Ok ok => Result.Success<
                ServerListing,
                ListServersError
            >(new ServerListing(ok.Value.Servers)),
            Result<ConfigDocument, ConfigStoreError>.Error err => Result.Failure<
                ServerListing,
                ListServersError
            >(new ListServersStoreFailure(err.Err)),
            _ => throw new InvalidOperationException("unknown Result variant"),
        };
    }
}
