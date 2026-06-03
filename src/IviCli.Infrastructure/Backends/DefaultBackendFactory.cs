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
    private readonly IIviBackend? _vxi11Backend;
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
        IIviBackend? socketBackend = null,
        IIviBackend? vxi11Backend = null
    )
    {
        _fallbackBackend = fallbackBackend;
        _localBackend = localBackend;
        _hislipBackend = hislipBackend;
        _socketBackend = socketBackend;
        _vxi11Backend = vxi11Backend;
    }

    /// <inheritdoc/>
    public Result<IIviBackend, BackendError> CreateFor(Device device)
    {
        // Scenario-aware short-circuit (issue #25, refined for HiSlip in
        // v0.2.1). When the user has activated a mock scenario, dispatches
        // that would otherwise hit a real transport backend with NOTHING
        // listening (placeholder VXI-11 / SOCKET / Local resources) are
        // re-routed to the FakeBackend instead, so the gateway answers
        // from scenes rather than timing out trying to TCP-connect to
        // 127.0.0.1:1024 or similar.
        //
        // HiSlip resources are deliberately EXCLUDED from the
        // short-circuit: a HiSlip resource string (`...::hislip0::INSTR`)
        // is the user explicitly asking to reach a network HiSLIP
        // endpoint, which is typically the ivi-cli gateway itself. If we
        // re-route HiSlip → FakeBackend on the client side, the
        // client process would answer the scenario locally instead of
        // crossing the wire to the gateway — every new ivicli
        // invocation would reset the FSM and FSM transitions would never
        // stick across CLI calls.
        var hasActiveScenario =
            _fallbackBackend is IScenarioAwareBackend probe && probe.HasActiveScenario;

        var backend = device.Resource switch
        {
            VisaResource.Tcpip t when LooksLikeHislip(t) => _hislipBackend ?? _fallbackBackend,
            VisaResource.Tcpip t when LooksLikeVxi11(t) && hasActiveScenario => _fallbackBackend,
            VisaResource.Tcpip t when LooksLikeVxi11(t) => _vxi11Backend ?? _fallbackBackend,
            VisaResource.TcpipSocket when hasActiveScenario => _fallbackBackend,
            VisaResource.TcpipSocket => _socketBackend ?? _fallbackBackend,
            VisaResource.Tcpip when hasActiveScenario => _fallbackBackend,
            VisaResource.Tcpip => _localBackend ?? _hislipBackend ?? _fallbackBackend,
            VisaResource.Usb when hasActiveScenario => _fallbackBackend,
            VisaResource.Usb => _localBackend ?? _fallbackBackend,
            VisaResource.Gpib when hasActiveScenario => _fallbackBackend,
            VisaResource.Gpib => _localBackend ?? _fallbackBackend,
            _ => _fallbackBackend,
        };

        return backend is null
            ? Result.Failure<IIviBackend, BackendError>(new UnsupportedTransport(device.Name))
            : Result.Success<IIviBackend, BackendError>(backend);
    }

    private static bool LooksLikeHislip(VisaResource.Tcpip resource) =>
        resource.LanDevice.StartsWith("hislip", StringComparison.Ordinal);

    private static bool LooksLikeVxi11(VisaResource.Tcpip resource) =>
        resource.LanDevice.StartsWith("inst", StringComparison.Ordinal);
}
