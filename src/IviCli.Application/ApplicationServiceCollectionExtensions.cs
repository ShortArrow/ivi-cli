using IviCli.Application.Auth;
using IviCli.Application.Devices;
using IviCli.Application.Diagnostics;
using IviCli.Application.Drivers;
using IviCli.Application.Mock;
using IviCli.Application.Scripting;
using IviCli.Application.Servers;
using IviCli.Application.Session;
using IviCli.Application.Watch;
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
        services.AddSingleton<IDeviceStatusProbe, DefaultDeviceStatusProbe>();
        services.AddSingleton<StatusDeviceCommandHandler>();
        services.AddSingleton<ScanDevicesQueryHandler>();
        services.AddSingleton<DiagnoseQueryHandler>();
        services.AddSingleton<ScriptDeviceCommandHandler>();
        services.AddSingleton<MonitorDeviceCommandHandler>();
        services.AddSingleton<IScriptLinter, DefaultScriptLinter>();
        services.AddSingleton<WatchDevicesCommandHandler>();
        services.AddSingleton<CreateApiTokenCommandHandler>();
        services.AddSingleton<ListApiTokensQueryHandler>();
        services.AddSingleton<RevokeApiTokenCommandHandler>();
        services.AddSingleton<ListDriversQueryHandler>();
        services.AddSingleton<ListLogicalNamesQueryHandler>();
        return services;
    }

    /// <summary>
    /// Registers mock-scenario handlers. The caller must already have an
    /// <see cref="IScenarioStore"/> registration (Infrastructure or test
    /// double).
    /// </summary>
    public static IServiceCollection AddIviCliMock(this IServiceCollection services)
    {
        services.AddSingleton<ListScenariosQueryHandler>();
        services.AddSingleton<CreateScenarioCommandHandler>();
        services.AddSingleton<RemoveScenarioCommandHandler>();
        services.AddSingleton<ShowScenarioQueryHandler>();
        services.AddSingleton<ActivateScenarioCommandHandler>();
        services.AddSingleton<DeactivateScenarioCommandHandler>();
        services.AddSingleton<AddSceneCommandHandler>();
        services.AddSingleton<RemoveSceneCommandHandler>();
        services.AddSingleton<RecordScenarioCommandHandler>();
        services.AddSingleton<ITrafficScenarioConverter, DefaultTrafficScenarioConverter>();
        services.AddSingleton<ImportScenarioFromTrafficCommandHandler>();
        return services;
    }

    /// <summary>Registers server / route management handlers.</summary>
    public static IServiceCollection AddIviCliServers(this IServiceCollection services)
    {
        services.AddSingleton<AddServerCommandHandler>();
        services.AddSingleton<RemoveServerCommandHandler>();
        services.AddSingleton<ListServersQueryHandler>();
        services.AddSingleton<AddRouteCommandHandler>();
        services.AddSingleton<RemoveRouteCommandHandler>();
        services.AddSingleton<ListRoutesQueryHandler>();
        services.AddSingleton<StopServerCommandHandler>();
        return services;
    }
}
