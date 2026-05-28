using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Plugin;

namespace IviCli.Infrastructure.Plugins;

/// <summary>
/// <see cref="IBackendFactory"/> decorator that consults plugin-
/// registered backends (ADR 0013) before delegating to the inner
/// factory. Plugin matchers run in registration order; the first
/// match wins.
/// </summary>
public sealed class PluginBackendFactory : IBackendFactory
{
    private readonly IBackendFactory _inner;
    private readonly IServiceProvider _services;
    private readonly IReadOnlyList<PluginBackendRegistration> _registrations;

    /// <summary>Creates a plugin-aware factory wrapping <paramref name="inner"/>.</summary>
    public PluginBackendFactory(
        IBackendFactory inner,
        IServiceProvider services,
        IReadOnlyList<PluginBackendRegistration> registrations
    )
    {
        _inner = inner;
        _services = services;
        _registrations = registrations;
    }

    /// <inheritdoc/>
    public Result<IIviBackend, BackendError> CreateFor(Device device)
    {
        foreach (var registration in _registrations)
        {
            if (registration.Matcher(device.Resource))
            {
                var backend = (IIviBackend?)_services.GetService(registration.BackendType);
                if (backend is null)
                {
                    return Result.Failure<IIviBackend, BackendError>(
                        new UnsupportedTransport(device.Name)
                    );
                }
                return Result.Success<IIviBackend, BackendError>(backend);
            }
        }
        return _inner.CreateFor(device);
    }
}

/// <summary>A single backend registration captured during <see cref="IIviPlugin.Register"/>.</summary>
public sealed record PluginBackendRegistration(Type BackendType, VisaResourceMatcher Matcher);
