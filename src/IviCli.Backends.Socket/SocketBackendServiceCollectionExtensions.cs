using IviCli.Application.Backends;
using Microsoft.Extensions.DependencyInjection;

namespace IviCli.Backends.Socket;

/// <summary>DI registration for <see cref="SocketBackend"/>.</summary>
public static class SocketBackendServiceCollectionExtensions
{
    /// <summary>Registers <see cref="SocketBackend"/> as an <see cref="IIviBackend"/>.</summary>
    public static IServiceCollection AddIviCliBackendsSocket(this IServiceCollection services)
    {
        services.AddSingleton<SocketBackend>();
        services.AddSingleton<IIviBackend>(sp => sp.GetRequiredService<SocketBackend>());
        return services;
    }
}
