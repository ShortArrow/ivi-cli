using IviCli.Application.Backends;
using Microsoft.Extensions.DependencyInjection;

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
}
