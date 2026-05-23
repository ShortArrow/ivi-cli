using IviCli.Application.Backends;
using Microsoft.Extensions.DependencyInjection;

namespace IviCli.Backends.HiSlip;

/// <summary>DI registration for <see cref="HiSlipBackend"/>.</summary>
public static class HiSlipBackendServiceCollectionExtensions
{
    /// <summary>Registers <see cref="HiSlipBackend"/> as an <see cref="IIviBackend"/>.</summary>
    public static IServiceCollection AddIviCliBackendsHiSlip(this IServiceCollection services)
    {
        services.AddSingleton<HiSlipBackend>();
        services.AddSingleton<IIviBackend>(sp => sp.GetRequiredService<HiSlipBackend>());
        return services;
    }
}
