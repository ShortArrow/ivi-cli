using System.Diagnostics.CodeAnalysis;
using IviCli.Application.Backends;
using IviCli.Plugin;
using Microsoft.Extensions.DependencyInjection;

namespace IviCli.Infrastructure.Plugins;

/// <summary>
/// <see cref="IPluginServices"/> implementation backed by an
/// <see cref="IServiceCollection"/> (ADR 0013). Plugin
/// <see cref="IIviPlugin.Register"/> calls accumulate concrete
/// backend types as singleton DI registrations + matcher records
/// the host stores on <see cref="Registrations"/> for later
/// consumption by <see cref="PluginBackendFactory"/>.
/// </summary>
public sealed class PluginServices : IPluginServices
{
    private readonly IServiceCollection _services;
    private readonly List<PluginBackendRegistration> _registrations = new();

    /// <summary>Creates a services adapter writing registrations into <paramref name="services"/>.</summary>
    public PluginServices(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>Snapshot of every backend registration captured so far.</summary>
    public IReadOnlyList<PluginBackendRegistration> Registrations => _registrations;

    /// <inheritdoc/>
    public void AddBackend<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TBackend
    >(VisaResourceMatcher matcher)
        where TBackend : class, IIviBackend
    {
        _services.AddSingleton<TBackend>();
        _registrations.Add(new PluginBackendRegistration(typeof(TBackend), matcher));
    }
}
