using IviCli.Application.Backends;
using Microsoft.Extensions.DependencyInjection;

namespace IviCli.Backends.Fake;

/// <summary>
/// DI registration entry-point for the Fake Backend (per ADR 0010 §6).
/// </summary>
public static class FakeBackendServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="FakeBackend"/> so tests and offline
    /// runs can resolve <see cref="IIviBackend"/> without real hardware.
    /// </summary>
    public static IServiceCollection AddIviCliBackendsFake(this IServiceCollection services)
    {
        services.AddSingleton<FakeBackend>();
        services.AddSingleton<IIviBackend>(sp => sp.GetRequiredService<FakeBackend>());
        services.AddSingleton<FakeBackendScanner>();
        services.AddSingleton<IBackendScanner>(sp => sp.GetRequiredService<FakeBackendScanner>());
        services.AddSingleton<IScenarioBindingRefresher, SessionScenarioBindingRefresher>();
        return services;
    }
}
