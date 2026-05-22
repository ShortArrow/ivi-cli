using IviCli.Domain.Devices;
using IviCli.Domain.Mock;

namespace IviCli.Domain.Session;

/// <summary>
/// Volatile per-invocation runtime state (the singleton Session in the domain
/// glossary). Holds pointers like the user's current device that change with
/// every <c>visa use</c> command. Persisted to <c>state.json</c> in the
/// platform state directory; for the static, human-editable defaults see
/// <c>ConfigDocument.Defaults</c>.
/// </summary>
/// <param name="CurrentDevice">
/// The alias of the device that subsequent commands operate on when no
/// explicit name is given. <see langword="null"/> means there is no
/// current device selected.
/// </param>
/// <param name="ActiveScenario">
/// The currently-activated mock scenario name, or <see langword="null"/>
/// when no scenario is active (ADR 0026 §2). The <c>IVICLI_SCENARIO</c>
/// environment variable takes precedence at runtime.
/// </param>
public sealed record SessionState(DeviceName? CurrentDevice, ScenarioName? ActiveScenario = null)
{
    /// <summary>The empty session state: no current device, no active scenario.</summary>
    public static SessionState Empty { get; } = new(CurrentDevice: null, ActiveScenario: null);
}
