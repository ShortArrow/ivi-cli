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
/// Every resource kind is named in the table below: HiSLIP, VXI-11, SOCKET,
/// VISA for the other TCPIP, USB and GPIB resources, and the serial
/// backends for ASRL (ADR 0055 §2). A kind no case names fails with
/// <see cref="UnsupportedTransport"/> rather than reaching the Fake backend.
/// </remarks>
public sealed class DefaultBackendFactory : IBackendFactory
{
    private readonly IIviBackend? _localBackend;
    private readonly IIviBackend? _hislipBackend;
    private readonly IIviBackend? _socketBackend;
    private readonly IIviBackend? _vxi11Backend;
    private readonly IIviBackend? _serialBackend;
    private readonly IIviBackend? _asrlBoardBackend;
    private readonly IIviBackend _fallbackBackend;

    /// <summary>
    /// Creates a factory with the supplied per-transport implementations.
    /// A transport the factory names but has no registration for falls
    /// through to <paramref name="fallbackBackend"/> (typically the Fake
    /// Backend), which is how <c>IVICLI_MOCK_ONLY</c> works.
    /// </summary>
    /// <param name="fallbackBackend">The backend for a transport with no registration.</param>
    /// <param name="localBackend">The VISA runtime backend.</param>
    /// <param name="hislipBackend">The HiSLIP client backend.</param>
    /// <param name="socketBackend">The raw SOCKET client backend.</param>
    /// <param name="vxi11Backend">The VXI-11 client backend.</param>
    /// <param name="serialBackend">The backend that opens a serial port by name (ADR 0055 §2).</param>
    /// <param name="asrlBoardBackend">
    /// The backend that looks an <c>ASRL&lt;board&gt;::INSTR</c> board up in the
    /// machine's <c>[[asrl]]</c> table (ADR 0055 §3).
    /// </param>
    public DefaultBackendFactory(
        IIviBackend fallbackBackend,
        IIviBackend? localBackend = null,
        IIviBackend? hislipBackend = null,
        IIviBackend? socketBackend = null,
        IIviBackend? vxi11Backend = null,
        IIviBackend? serialBackend = null,
        IIviBackend? asrlBoardBackend = null
    )
    {
        _fallbackBackend = fallbackBackend;
        _localBackend = localBackend;
        _hislipBackend = hislipBackend;
        _socketBackend = socketBackend;
        _vxi11Backend = vxi11Backend;
        _serialBackend = serialBackend;
        _asrlBoardBackend = asrlBoardBackend;
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
        // v0.2.4 per-device shape (issue #36): only short-circuit when
        // *this specific device* has a scenario bound. Other devices on
        // the same gateway may still hit their real transport backend.
        var hasActiveScenario =
            _fallbackBackend is IScenarioAwareBackend probe && probe.HasActiveScenarioFor(device);

        IIviBackend? backend = device.Resource switch
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
            VisaResource.AsrlBoard or VisaResource.AsrlPort when hasActiveScenario =>
                _fallbackBackend,
            VisaResource.AsrlPort => _serialBackend ?? _fallbackBackend,
            VisaResource.AsrlBoard => _asrlBoardBackend ?? _fallbackBackend,
            // A resource kind no case names has no backend; it fails rather
            // than answering from the Fake backend (ADR 0055 §2).
            _ => null,
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
