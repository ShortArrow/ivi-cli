using System.IO.Abstractions;
using IviCli.Application.Configuration;
using IviCli.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IviCli.Infrastructure;

/// <summary>
/// DI registration entry-point for the Infrastructure layer (per ADR 0010 §6).
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers the production <see cref="IFileSystem"/> and the
    /// <see cref="TomlConfigStore"/> bound to the supplied <paramref name="configPath"/>.
    /// </summary>
    /// <param name="services">The service collection to mutate.</param>
    /// <param name="configPath">Absolute path to the <c>config.toml</c> file.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddIviCliInfrastructure(
        this IServiceCollection services,
        string configPath
    )
    {
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<IConfigStore>(sp => new TomlConfigStore(
            sp.GetRequiredService<IFileSystem>(),
            configPath
        ));
        return services;
    }
}
