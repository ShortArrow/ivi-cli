using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Servers;

namespace IviCli.Application.Servers;

/// <summary>
/// Long-running gateway-server port. One implementation exists per
/// <see cref="ServerType"/>; the composition root selects the right one
/// via <see cref="IGatewayServerFactory"/>.
/// </summary>
/// <remarks>
/// <see cref="RunAsync"/> blocks until the supplied
/// <see cref="CancellationToken"/> fires or a fatal error occurs.
/// Graceful shutdown is the cancellation path (ADR 0015 §5).
/// </remarks>
public interface IGatewayServer
{
    /// <summary>The protocol this implementation serves.</summary>
    ServerType SupportedType { get; }

    /// <summary>
    /// Runs the gateway listener and returns when cancelled or on fatal error.
    /// </summary>
    /// <param name="server">The configured server entity (bind / port / type).</param>
    /// <param name="config">The full configuration (read-only) for routing.</param>
    /// <param name="ct">Cancellation token tied to graceful shutdown.</param>
    Task<Result<Unit, GatewayServerError>> RunAsync(
        Server server,
        ConfigDocument config,
        CancellationToken ct
    );
}

/// <summary>
/// Selects the <see cref="IGatewayServer"/> implementation that handles a
/// given <see cref="ServerType"/>.
/// </summary>
public interface IGatewayServerFactory
{
    /// <summary>Returns the implementation that handles <paramref name="type"/>.</summary>
    Result<IGatewayServer, GatewayServerError> CreateFor(ServerType type);
}

/// <summary>Errors that can arise from gateway-server lifecycle operations.</summary>
public abstract record GatewayServerError : IviError
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

/// <summary>No implementation is registered for the requested protocol.</summary>
public sealed record UnsupportedServerType(ServerType Type) : GatewayServerError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Error;

    /// <inheritdoc/>
    public override string Message => "no gateway implementation for type {Type}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Type };
}

/// <summary>The TCP listener could not be bound.</summary>
public sealed record GatewayBindFailure(
    IpAddress Bind,
    Port Port,
    string Reason,
    Exception? InnerException = null
) : GatewayServerError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Error;

    /// <inheritdoc/>
    public override string Message => "gateway bind failed at {Bind}:{Port} — {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Bind, Port, Reason };

    /// <inheritdoc/>
    public override Exception? Cause => InnerException;
}
