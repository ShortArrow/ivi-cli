using IviCli.Application.Configuration;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Servers;

namespace IviCli.Application.Servers;

/// <summary>Command DTO for starting a configured gateway server.</summary>
public sealed record StartServerCommand(string Name);

/// <summary>Outcomes the start command can fail with.</summary>
public abstract record StartServerError : IviError
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
public sealed record StartServerInvalidName(string Raw) : StartServerError
{
    public override LogSeverity Severity => LogSeverity.Warning;
    public override string Message => "invalid server name: {Raw}";
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The named server is not registered.</summary>
public sealed record StartServerUnknown(ServerName Name) : StartServerError
{
    public override LogSeverity Severity => LogSeverity.Warning;
    public override string Message => "server not found: {Name}";
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>Storage failure during start.</summary>
public sealed record StartServerStoreFailure(ConfigStoreError Inner) : StartServerError
{
    public override LogSeverity Severity => Inner.Severity;
    public override string Message => Inner.Message;
    public override IReadOnlyList<object?> LogArgs => Inner.LogArgs;
    public override Exception? Cause => Inner.Cause;
}

/// <summary>Gateway-server lifecycle failure.</summary>
public sealed record StartServerLifecycleFailure(GatewayServerError Inner) : StartServerError
{
    public override LogSeverity Severity => Inner.Severity;
    public override string Message => Inner.Message;
    public override IReadOnlyList<object?> LogArgs => Inner.LogArgs;
    public override Exception? Cause => Inner.Cause;
}

/// <summary>
/// Application-layer handler for <c>server start</c>. Resolves the named
/// server from the config, picks the appropriate <see cref="IGatewayServer"/>
/// via <see cref="IGatewayServerFactory"/>, and blocks until cancellation.
/// </summary>
public sealed class StartServerCommandHandler
{
    private readonly IConfigStore _configStore;
    private readonly IGatewayServerFactory _factory;
    private readonly IServerProcessRegistry _registry;
    private readonly TimeProvider _time;

    /// <summary>Creates a new handler.</summary>
    public StartServerCommandHandler(
        IConfigStore configStore,
        IGatewayServerFactory factory,
        IServerProcessRegistry registry,
        TimeProvider? time = null
    )
    {
        _configStore = configStore;
        _factory = factory;
        _registry = registry;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Runs the requested server.</summary>
    public async Task<Result<Unit, StartServerError>> HandleAsync(
        StartServerCommand command,
        CancellationToken ct
    )
    {
        if (
            ServerName.From(command.Name)
            is not Result<ServerName, ServerNameError>.Ok { Value: var name }
        )
        {
            return Result.Failure<Unit, StartServerError>(new StartServerInvalidName(command.Name));
        }

        var loadResult = await _configStore.LoadAsync(ct);
        if (loadResult is not Result<ConfigDocument, ConfigStoreError>.Ok { Value: var config })
        {
            var err = ((Result<ConfigDocument, ConfigStoreError>.Error)loadResult).Err;
            return Result.Failure<Unit, StartServerError>(new StartServerStoreFailure(err));
        }

        var server = config.FindServer(name);
        if (server is null)
        {
            return Result.Failure<Unit, StartServerError>(new StartServerUnknown(name));
        }

        var gatewayResult = _factory.CreateFor(server.Type);
        if (
            gatewayResult
            is not Result<IGatewayServer, GatewayServerError>.Ok { Value: var gateway }
        )
        {
            var err = ((Result<IGatewayServer, GatewayServerError>.Error)gatewayResult).Err;
            return Result.Failure<Unit, StartServerError>(new StartServerLifecycleFailure(err));
        }

        var writeResult = await _registry.WriteAsync(
            name,
            Environment.ProcessId,
            _time.GetUtcNow(),
            ct
        );
        if (writeResult is Result<Unit, ServerProcessRegistryError>.Error writeErr)
        {
            return Result.Failure<Unit, StartServerError>(
                new StartServerRegistryFailure(writeErr.Err)
            );
        }

        try
        {
            var runResult = await gateway.RunAsync(server, config, ct);
            return runResult switch
            {
                Result<Unit, GatewayServerError>.Ok ok => Result.Success<Unit, StartServerError>(
                    ok.Value
                ),
                Result<Unit, GatewayServerError>.Error err => Result.Failure<
                    Unit,
                    StartServerError
                >(new StartServerLifecycleFailure(err.Err)),
                _ => throw new InvalidOperationException("unknown Result variant"),
            };
        }
        finally
        {
            // Best-effort cleanup; if the file system rejects the delete the
            // next start overwrites the PID file anyway.
            _ = await _registry.DeleteAsync(name, ct);
        }
    }
}

/// <summary>The PID registry failed to record this server's process.</summary>
public sealed record StartServerRegistryFailure(ServerProcessRegistryError Inner) : StartServerError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => Inner.Severity;

    /// <inheritdoc/>
    public override string Message => Inner.Message;

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => Inner.LogArgs;

    /// <inheritdoc/>
    public override Exception? Cause => Inner.Cause;
}
