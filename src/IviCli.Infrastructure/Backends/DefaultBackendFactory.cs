using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;

namespace IviCli.Infrastructure.Backends;

/// <summary>
/// Routes a <see cref="Device"/> to the appropriate <see cref="IIviBackend"/>
/// implementation based on its VISA resource variant (per ADR 0010 §4).
/// </summary>
/// <remarks>
/// Phase 1 ships only the Fake Backend. Real Backends (Local NI-VISA,
/// HiSLIP, Socket, Replay) register themselves with the factory as they
/// land; the resolution table below grows correspondingly.
/// </remarks>
public sealed class DefaultBackendFactory : IBackendFactory
{
    private readonly IIviBackend? _localBackend;
    private readonly IIviBackend? _hislipBackend;
    private readonly IIviBackend? _socketBackend;
    private readonly IIviBackend _fallbackBackend;

    /// <summary>
    /// Creates a factory with the supplied per-transport implementations.
    /// Any transport without a registration falls through to
    /// <paramref name="fallbackBackend"/> (typically the Fake Backend).
    /// </summary>
    public DefaultBackendFactory(
        IIviBackend fallbackBackend,
        IIviBackend? localBackend = null,
        IIviBackend? hislipBackend = null,
        IIviBackend? socketBackend = null
    )
    {
        _fallbackBackend = fallbackBackend;
        _localBackend = localBackend;
        _hislipBackend = hislipBackend;
        _socketBackend = socketBackend;
    }

    /// <inheritdoc/>
    public Result<IIviBackend, BackendError> CreateFor(Device device)
    {
        var backend = device.Resource switch
        {
            VisaResource.Tcpip t when LooksLikeHislip(t) => _hislipBackend ?? _fallbackBackend,
            VisaResource.Tcpip => _localBackend ?? _hislipBackend ?? _fallbackBackend,
            VisaResource.Usb => _localBackend ?? _fallbackBackend,
            VisaResource.Gpib => _localBackend ?? _fallbackBackend,
            _ => _fallbackBackend,
        };

        return backend is null
            ? Result.Failure<IIviBackend, BackendError>(new UnsupportedTransport(device.Name))
            : Result.Success<IIviBackend, BackendError>(backend);
    }

    private static bool LooksLikeHislip(VisaResource.Tcpip resource) =>
        resource.LanDevice.StartsWith("hislip", StringComparison.Ordinal);
}
