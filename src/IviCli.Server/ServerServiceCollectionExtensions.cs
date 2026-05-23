using IviCli.Application.Servers;
using IviCli.Server.HiSlip;
using IviCli.Server.Socket;
using Microsoft.Extensions.DependencyInjection;

namespace IviCli.Server;

/// <summary>
/// DI registration entry-point for the gateway-server implementations
/// (per ADR 0010 §6). Registers the SOCKET and HiSLIP implementations.
/// </summary>
public static class ServerServiceCollectionExtensions
{
    /// <summary>Registers every supported gateway-server implementation plus the factory.</summary>
    public static IServiceCollection AddIviCliGatewayServers(this IServiceCollection services)
    {
        services.AddSingleton<SocketGatewayServer>();
        services.AddSingleton<IGatewayServer>(sp => sp.GetRequiredService<SocketGatewayServer>());
        services.AddSingleton<HiSlipGatewayServer>();
        services.AddSingleton<IGatewayServer>(sp => sp.GetRequiredService<HiSlipGatewayServer>());
        services.AddSingleton<IGatewayServerFactory>(sp => new DefaultGatewayServerFactory(
            sp.GetServices<IGatewayServer>()
        ));
        services.AddSingleton<StartServerCommandHandler>();
        return services;
    }
}
