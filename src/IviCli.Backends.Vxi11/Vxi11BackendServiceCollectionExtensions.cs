using IviCli.Application.Backends;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IviCli.Backends.Vxi11;

/// <summary>DI registration for <see cref="Vxi11Backend"/>.</summary>
public static class Vxi11BackendServiceCollectionExtensions
{
    /// <summary>Registers <see cref="Vxi11Backend"/> as an <see cref="IIviBackend"/>.</summary>
    public static IServiceCollection AddIviCliBackendsVxi11(this IServiceCollection services)
    {
        services.AddSingleton<Vxi11Backend>();
        services.AddSingleton<IIviBackend>(sp => sp.GetRequiredService<Vxi11Backend>());
        return services;
    }

    /// <summary>
    /// Registers the VXI-11 portmapper broadcast scanner (ADR 0008,
    /// Batch W). Available even when no VXI-11 device has been added
    /// to config — discovery is the point.
    /// </summary>
    public static IServiceCollection AddIviCliVxi11Scanner(this IServiceCollection services)
    {
        services.AddSingleton<Vxi11BroadcastScanner>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IBackendScanner, Vxi11BroadcastScanner>(sp =>
                sp.GetRequiredService<Vxi11BroadcastScanner>()
            )
        );
        return services;
    }
}
