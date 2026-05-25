using IviCli.Application.Backends;
using Microsoft.Extensions.DependencyInjection;

namespace IviCli.Backends.Local;

/// <summary>DI registration for <see cref="LocalBackend"/>.</summary>
public static class LocalBackendServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="LocalBackend"/> backed by
    /// <see cref="ReflectionVisaSessionFactory"/>. Override the
    /// <see cref="IVisaSessionFactory"/> registration before calling
    /// this to swap the VISA loader for tests.
    /// </summary>
    public static IServiceCollection AddIviCliBackendsLocal(this IServiceCollection services)
    {
        services.AddSingleton<IVisaSessionFactory, ReflectionVisaSessionFactory>();
        services.AddSingleton<LocalBackend>();
        services.AddSingleton<IIviBackend>(sp => sp.GetRequiredService<LocalBackend>());
        return services;
    }
}
