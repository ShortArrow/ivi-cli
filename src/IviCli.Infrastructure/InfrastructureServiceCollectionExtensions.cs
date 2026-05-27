using System.IO.Abstractions;
using IviCli.Application.Backends;
using IviCli.Application.Capture;
using IviCli.Application.Configuration;
using IviCli.Application.Mock;
using IviCli.Application.Servers;
using IviCli.Application.Session;
using IviCli.Infrastructure.Backends;
using IviCli.Infrastructure.Configuration;
using IviCli.Infrastructure.Mock;
using IviCli.Infrastructure.Servers;
using IviCli.Infrastructure.Session;
using Microsoft.Extensions.DependencyInjection;

namespace IviCli.Infrastructure;

/// <summary>
/// DI registration entry-point for the Infrastructure layer (per ADR 0010 §6).
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers the production <see cref="IFileSystem"/>, the
    /// <see cref="TomlConfigStore"/> bound to <paramref name="configPath"/>,
    /// and the <see cref="JsonSessionStore"/> bound to <paramref name="sessionPath"/>
    /// (a sibling of <paramref name="configPath"/> when <see langword="null"/>).
    /// </summary>
    /// <param name="services">The service collection to mutate.</param>
    /// <param name="configPath">Absolute path to the <c>config.toml</c> file.</param>
    /// <param name="sessionPath">
    /// Absolute path to the <c>session.json</c> file. When <see langword="null"/>
    /// a sibling of <paramref name="configPath"/> is used.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddIviCliInfrastructure(
        this IServiceCollection services,
        string configPath,
        string? sessionPath = null
    )
    {
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<IConfigStore>(sp => new TomlConfigStore(
            sp.GetRequiredService<IFileSystem>(),
            configPath
        ));
        services.AddSingleton<ISessionStore>(sp => new JsonSessionStore(
            sp.GetRequiredService<IFileSystem>(),
            sessionPath ?? DeriveDefaultSessionPath(configPath)
        ));
        services.AddSingleton<ITrafficWriter>(NullTrafficWriter.Instance);
        services.AddSingleton<INdjsonTrafficReader>(sp => new Capture.NdjsonTrafficReader(
            sp.GetRequiredService<IFileSystem>()
        ));
        return services;
    }

    private static string DeriveDefaultSessionPath(string configPath)
    {
        var directory = Path.GetDirectoryName(configPath) ?? ".";
        return Path.Combine(directory, "session.json");
    }

    /// <summary>
    /// Registers the <see cref="IServerProcessRegistry"/> implementation
    /// backed by <see cref="FilePidRegistry"/> rooted at
    /// <paramref name="serverStateDirectory"/>.
    /// </summary>
    public static IServiceCollection AddIviCliServerProcessRegistry(
        this IServiceCollection services,
        string serverStateDirectory
    )
    {
        services.AddSingleton<IServerProcessRegistry>(sp => new FilePidRegistry(
            sp.GetRequiredService<IFileSystem>(),
            serverStateDirectory
        ));
        return services;
    }

    /// <summary>
    /// Registers <see cref="DefaultBackendFactory"/> as the default
    /// <see cref="IBackendFactory"/>. The caller is responsible for having
    /// already registered at least one <see cref="IIviBackend"/>
    /// implementation (typically <c>AddIviCliBackendsFake()</c> or, in the
    /// future, <c>AddIviCliBackendsLocal()</c>).
    /// </summary>
    public static IServiceCollection AddIviCliBackendFactory(this IServiceCollection services)
    {
        services.AddSingleton<IBackendFactory>(sp =>
        {
            var fallback = sp.GetRequiredService<IIviBackend>();
            return new DefaultBackendFactory(fallback);
        });
        return services;
    }

    /// <summary>
    /// Registers <see cref="TomlScenarioStore"/> as the default
    /// <see cref="IScenarioStore"/>. <paramref name="scenarioDirectory"/>
    /// may be <see langword="null"/> to default to a <c>scenarios/</c>
    /// sibling of the supplied config path.
    /// </summary>
    public static IServiceCollection AddIviCliScenarioStore(
        this IServiceCollection services,
        string configPath,
        string? scenarioDirectory = null
    )
    {
        var directory = scenarioDirectory ?? DeriveDefaultScenarioDirectory(configPath);
        services.AddSingleton<IScenarioStore>(sp => new TomlScenarioStore(
            sp.GetRequiredService<IFileSystem>(),
            directory
        ));
        return services;
    }

    private static string DeriveDefaultScenarioDirectory(string configPath)
    {
        var directory = Path.GetDirectoryName(configPath) ?? ".";
        return Path.Combine(directory, "scenarios");
    }
}
