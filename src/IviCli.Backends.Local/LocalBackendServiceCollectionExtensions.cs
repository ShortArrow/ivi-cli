using IviCli.Application.Backends;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IviCli.Backends.Local;

/// <summary>DI registration for <see cref="LocalBackend"/>.</summary>
public static class LocalBackendServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="LocalBackend"/> backed by
    /// <see cref="VisaSessionFactory"/>. Override the
    /// <see cref="IVisaSessionFactory"/> registration before calling
    /// this to swap the VISA loader for tests.
    /// </summary>
    public static IServiceCollection AddIviCliBackendsLocal(this IServiceCollection services)
    {
        services.AddSingleton<IVisaSessionFactory, VisaSessionFactory>();
        services.AddSingleton<LocalBackend>();
        services.AddSingleton<IIviBackend>(sp => sp.GetRequiredService<LocalBackend>());
        return services;
    }

    /// <summary>
    /// Registers <see cref="LocalUsbScanner"/> as an additional
    /// <see cref="IBackendScanner"/>, backed by
    /// <see cref="VisaResourceFinder"/>, so <c>ivicli visa scan</c>
    /// surfaces USB instruments visible to the installed VISA runtime.
    /// </summary>
    public static IServiceCollection AddIviCliLocalUsbScanner(this IServiceCollection services)
    {
        services.TryAddSingleton<IVisaResourceFinder, VisaResourceFinder>();
        services.AddSingleton<LocalUsbScanner>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IBackendScanner, LocalUsbScanner>(sp =>
                sp.GetRequiredService<LocalUsbScanner>()
            )
        );
        return services;
    }
}
