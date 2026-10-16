using IviCli.Application.Backends;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IviCli.Backends.Serial;

/// <summary>DI registration for <see cref="SerialBackend"/>.</summary>
public static class SerialBackendServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="SerialBackend"/>, over real serial ports, as an
    /// <see cref="IIviBackend"/>.
    /// </summary>
    public static IServiceCollection AddIviCliBackendsSerial(this IServiceCollection services)
    {
        services.AddSingleton<ISerialLineFactory, SerialPortLineFactory>();
        services.AddSingleton<SerialBackend>();
        services.AddSingleton<IIviBackend>(sp => sp.GetRequiredService<SerialBackend>());
        return services;
    }

    /// <summary>
    /// Registers <see cref="SerialPortScanner"/> as an additional
    /// <see cref="IBackendScanner"/>, so <c>ivicli visa scan</c> lists the
    /// machine's serial ports.
    /// </summary>
    public static IServiceCollection AddIviCliSerialScanner(this IServiceCollection services)
    {
        services.AddSingleton<SerialPortScanner>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IBackendScanner, SerialPortScanner>(sp =>
                sp.GetRequiredService<SerialPortScanner>()
            )
        );
        return services;
    }
}
