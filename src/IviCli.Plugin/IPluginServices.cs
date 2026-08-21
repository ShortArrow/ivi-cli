using System.Diagnostics.CodeAnalysis;
using IviCli.Application.Backends;
using IviCli.Domain.Visa;

namespace IviCli.Plugin;

/// <summary>
/// Narrow registration surface ivi-cli hands to a plugin's
/// <see cref="IIviPlugin.Register"/> method. Plugins use this to
/// publish their <see cref="IIviBackend"/> implementations along
/// with a matcher that decides which <see cref="VisaResource"/>
/// values they handle (ADR 0013).
/// </summary>
public interface IPluginServices
{
    /// <summary>
    /// Registers a backend implementation handled by
    /// <paramref name="matcher"/>. The host's resolved
    /// <c>IBackendFactory</c> consults plugin-registered backends
    /// before falling back to its built-in routing table.
    /// </summary>
    /// <typeparam name="TBackend">The plugin's backend concrete type.</typeparam>
    /// <param name="matcher">Predicate over the device's VISA resource.</param>
    void AddBackend<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TBackend
    >(VisaResourceMatcher matcher)
        where TBackend : class, IIviBackend;
}

/// <summary>
/// Predicate over a <see cref="VisaResource"/>. Plugins return
/// <see langword="true"/> when the resource should route to their
/// backend; the host evaluates plugin matchers in registration
/// order and uses the first match.
/// </summary>
public delegate bool VisaResourceMatcher(VisaResource resource);
