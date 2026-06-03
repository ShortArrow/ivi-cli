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
    /// True iff a scenario is currently active on this backend. The
    /// flag is allowed to flip at runtime (scenario activate/deactivate
    /// commands); implementations should read it lazily, not cache it.
    /// </summary>
    bool HasActiveScenario { get; }
}
