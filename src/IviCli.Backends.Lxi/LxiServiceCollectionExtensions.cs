using IviCli.Application.Backends;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IviCli.Backends.Lxi;

/// <summary>
/// Adds the LXI mDNS scanner (ADR 0008, Batch W) as an
/// <see cref="IBackendScanner"/> so <c>ivicli visa scan</c> can
/// surface LXI-conformant instruments without any operator-side
/// configuration.
/// </summary>
public static class LxiServiceCollectionExtensions
{
    /// <summary>Registers the LXI mDNS scanner.</summary>
    public static IServiceCollection AddIviCliLxiScanner(this IServiceCollection services)
    {
        services.AddSingleton<LxiMdnsScanner>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IBackendScanner, LxiMdnsScanner>(sp =>
                sp.GetRequiredService<LxiMdnsScanner>()
            )
        );
        return services;
    }
}
