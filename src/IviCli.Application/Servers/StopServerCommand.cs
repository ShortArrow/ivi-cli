using IviCli.Application.Configuration;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Servers;

namespace IviCli.Application.Servers;

/// <summary>Command DTO for stopping a configured gateway server.</summary>
public sealed record StopServerCommand(string Name);

/// <summary>Errors emitted by <see cref="StopServerCommandHandler"/>.</summary>
public abstract record StopServerError : IviError
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
public sealed record StopServerInvalidName(string Raw) : StopServerError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid server name: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The named server is not configured.</summary>
public sealed record StopServerUnknown(ServerName Name) : StopServerError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "server not found: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>No PID file exists; the server is not running.</summary>
public sealed record StopServerNotRunning(ServerName Name) : StopServerError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "server not running: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>Config store failure.</summary>
public sealed record StopServerStoreFailure(ConfigStoreError Inner) : StopServerError
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

/// <summary>Process registry failure.</summary>
public sealed record StopServerRegistryFailure(ServerProcessRegistryError Inner) : StopServerError
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

/// <summary>
/// Resolves the running PID for a configured server. The Application layer
/// returns the entry; the CLI layer performs the OS-specific termination
/// (Process.Kill / CtrlBreak) and removes the registry entry on success.
/// </summary>
public sealed class StopServerCommandHandler
{
    private readonly IConfigStore _configStore;
    private readonly IServerProcessRegistry _registry;

    /// <summary>Creates a handler bound to the supplied dependencies.</summary>
    public StopServerCommandHandler(IConfigStore configStore, IServerProcessRegistry registry)
    {
        _configStore = configStore;
        _registry = registry;
    }

    /// <summary>Returns the registered entry, or a structured failure.</summary>
    public async Task<Result<ServerProcessEntry, StopServerError>> HandleAsync(
        StopServerCommand command,
        CancellationToken ct
    )
    {
        if (
            ServerName.From(command.Name)
            is not Result<ServerName, ServerNameError>.Ok { Value: var name }
        )
        {
            return Result.Failure<ServerProcessEntry, StopServerError>(
                new StopServerInvalidName(command.Name)
            );
        }

        var configResult = await _configStore.LoadAsync(ct);
        if (configResult is not Result<ConfigDocument, ConfigStoreError>.Ok { Value: var config })
        {
            var err = ((Result<ConfigDocument, ConfigStoreError>.Error)configResult).Err;
            return Result.Failure<ServerProcessEntry, StopServerError>(
                new StopServerStoreFailure(err)
            );
        }
        if (config.FindServer(name) is null)
        {
            return Result.Failure<ServerProcessEntry, StopServerError>(new StopServerUnknown(name));
        }

        var readResult = await _registry.ReadAsync(name, ct);
        if (
            readResult
            is not Result<ServerProcessEntry?, ServerProcessRegistryError>.Ok { Value: var entry }
        )
        {
            var err = (
                (Result<ServerProcessEntry?, ServerProcessRegistryError>.Error)readResult
            ).Err;
            return Result.Failure<ServerProcessEntry, StopServerError>(
                new StopServerRegistryFailure(err)
            );
        }
        if (entry is null)
        {
            return Result.Failure<ServerProcessEntry, StopServerError>(
                new StopServerNotRunning(name)
            );
        }

        return Result.Success<ServerProcessEntry, StopServerError>(entry);
    }

    /// <summary>Removes the registry entry once the OS kill succeeded.</summary>
    public Task<Result<Unit, ServerProcessRegistryError>> ClearEntryAsync(
        ServerName name,
        CancellationToken ct
    ) => _registry.DeleteAsync(name, ct);
}
