using IviCli.Application.Backends;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    /// <summary>
    /// Registers the active socket-sweep scanner and the shared endpoint prober
    /// used by both the sweep and the <c>visa scan</c> host-enrichment pass
    /// (ADR 0008). The prober is required by the scan handler regardless of
    /// whether any SOCKET device is configured — discovery is the point.
    /// </summary>
    public static IServiceCollection AddIviCliSocketScanner(this IServiceCollection services)
    {
        services.TryAddSingleton<IEndpointProber, SocketEndpointProber>();
        services.AddSingleton<SocketSweepScanner>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IBackendScanner, SocketSweepScanner>(sp =>
                sp.GetRequiredService<SocketSweepScanner>()
            )
        );
        return services;
    }
}
