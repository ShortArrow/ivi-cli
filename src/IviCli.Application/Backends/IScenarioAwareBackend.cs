using IviCli.Domain.Devices;

namespace IviCli.Application.Backends;

/// <summary>
/// Optional capability mixin for backends whose response is driven by
/// an active mock scenario. <see cref="IBackendFactory"/> implementations
/// consult this on the fallback / mock backend to decide whether the
/// scenario should outrank resource-shape dispatch (see ADR 0026 +
/// issue #25):
///
/// When a scenario is active, the user has explicitly asked the mock
/// backend to answer for every routed device, regardless of whether the
/// device's <c>VisaResource</c> shape looks like a real instrument
/// (e.g. <c>TCPIP0::host::INSTR</c> normally routes to VXI-11). Forcing
/// the dispatch to the scenario-backed backend avoids the gateway
/// trying — and timing out on — a real transport connection that
/// nothing is listening to.
/// </summary>
public interface IScenarioAwareBackend : IIviBackend
{
    /// <summary>
    /// True iff at least one device has an active scenario binding on
    /// this backend. The flag is allowed to flip at runtime (scenario
    /// activate/deactivate commands); implementations should read it
    /// lazily, not cache it.
    /// </summary>
    bool HasActiveScenario { get; }

    /// <summary>
    /// True iff <paramref name="device"/> specifically has an active
    /// scenario binding. v0.2.4+ replaced the single-global-scenario
    /// model with per-device bindings (issue #36) — factories must short-
    /// circuit to this backend only for devices that *actually* have a
    /// scenario, not unconditionally whenever any scenario is active.
    /// </summary>
    bool HasActiveScenarioFor(Device device);
}
