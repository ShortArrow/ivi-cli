using IviCli.Application.Backends;
using Microsoft.Extensions.DependencyInjection;

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
}
