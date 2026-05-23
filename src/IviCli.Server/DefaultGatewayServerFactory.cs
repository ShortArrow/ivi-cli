using IviCli.Application.Servers;
using IviCli.Domain;
using IviCli.Domain.Servers;

namespace IviCli.Server;

/// <summary>
/// Resolves <see cref="IGatewayServer"/> implementations by
/// <see cref="ServerType"/>. The composition root registers all available
/// implementations via DI; this factory dispatches between them.
/// </summary>
public sealed class DefaultGatewayServerFactory : IGatewayServerFactory
{
    private readonly IEnumerable<IGatewayServer> _servers;

    /// <summary>Creates a factory bound to the supplied implementations.</summary>
    public DefaultGatewayServerFactory(IEnumerable<IGatewayServer> servers)
    {
        _servers = servers;
    }

    /// <inheritdoc/>
    public Result<IGatewayServer, GatewayServerError> CreateFor(ServerType type)
    {
        foreach (var server in _servers)
        {
            if (server.SupportedType == type)
            {
                return Result.Success<IGatewayServer, GatewayServerError>(server);
            }
        }
        return Result.Failure<IGatewayServer, GatewayServerError>(new UnsupportedServerType(type));
    }
}
