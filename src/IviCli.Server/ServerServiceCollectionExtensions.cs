using IviCli.Application.Servers;
using IviCli.Server.HiSlip;
using IviCli.Server.Socket;
using IviCli.Server.UsbIp;
using IviCli.Server.Vxi11;
using Microsoft.Extensions.DependencyInjection;

namespace IviCli.Server;

/// <summary>
/// DI registration entry-point for the gateway-server implementations
/// (per ADR 0010 §6). Registers the SOCKET, HiSLIP, VXI-11, and USB/IP
/// implementations behind the shared <see cref="IGatewayServerFactory"/>.
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
        services.AddSingleton<Vxi11GatewayServer>();
        services.AddSingleton<IGatewayServer>(sp => sp.GetRequiredService<Vxi11GatewayServer>());
        services.AddSingleton<UsbIpGatewayServer>();
        services.AddSingleton<IGatewayServer>(sp => sp.GetRequiredService<UsbIpGatewayServer>());
        services.AddSingleton<IGatewayServerFactory>(sp => new DefaultGatewayServerFactory(
            sp.GetServices<IGatewayServer>()
        ));
        services.AddSingleton<StartServerCommandHandler>();
        return services;
    }
}
