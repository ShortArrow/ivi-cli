using IviCli.Application.Backends;
using IviCli.Domain.Mock;
using Microsoft.Extensions.DependencyInjection;

namespace IviCli.Backends.Replay;

/// <summary>DI registration for <see cref="ReplayBackend"/>.</summary>
public static class ReplayBackendServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ReplayBackend"/> bound to the supplied
    /// <paramref name="scenario"/>. The CLI composition root calls this
    /// when <c>IVICLI_REPLAY</c> is set so all device traffic is routed
    /// to the playback backend.
    /// </summary>
    public static IServiceCollection AddIviCliBackendsReplay(
        this IServiceCollection services,
        MockScenario scenario
    )
    {
        var backend = new ReplayBackend(scenario);
        services.AddSingleton(backend);
        services.AddSingleton<IIviBackend>(backend);
        return services;
    }
}
