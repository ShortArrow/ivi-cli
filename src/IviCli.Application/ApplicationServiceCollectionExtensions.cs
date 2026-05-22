using IviCli.Application.Devices;
using IviCli.Application.Session;
using Microsoft.Extensions.DependencyInjection;

namespace IviCli.Application;

/// <summary>
/// DI registration entry-point for the Application layer (per ADR 0010 §6).
/// The composition root calls <see cref="AddIviCliApplication"/> from
/// <c>IviCli.Cli/Program.cs</c>.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers Application-layer use-case handlers. The caller must also
    /// register an <see cref="Configuration.IConfigStore"/> implementation
    /// (Infrastructure layer or a test substitute).
    /// </summary>
    /// <param name="services">The service collection to mutate.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddIviCliApplication(this IServiceCollection services)
    {
        services.AddSingleton<AddDeviceCommandHandler>();
        services.AddSingleton<RemoveDeviceCommandHandler>();
        services.AddSingleton<ListDevicesQueryHandler>();
        services.AddSingleton<SetCurrentDeviceCommandHandler>();
        services.AddSingleton<GetCurrentDeviceQueryHandler>();
        services.AddSingleton<QueryDeviceCommandHandler>();
        services.AddSingleton<WriteDeviceCommandHandler>();
        services.AddSingleton<ReadDeviceCommandHandler>();
        services.AddSingleton<StatusDeviceCommandHandler>();
        services.AddSingleton<ScanDevicesQueryHandler>();
        return services;
    }
}
