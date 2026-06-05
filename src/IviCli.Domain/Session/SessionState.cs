using System.Collections.Immutable;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;

namespace IviCli.Domain.Session;

/// <summary>
/// Volatile per-invocation runtime state (the singleton Session in the
/// domain glossary). Holds pointers like the user's current device that
/// change with every <c>visa use</c> command. Persisted to <c>state.json</c>
/// in the platform state directory; for the static, human-editable
/// defaults see <c>ConfigDocument.Defaults</c>.
/// </summary>
/// <param name="CurrentDevice">
/// The alias of the device that subsequent commands operate on when no
/// explicit name is given. <see langword="null"/> means there is no
/// current device selected.
/// </param>
/// <param name="DeviceScenarios">
/// Per-device active mock scenario bindings (ADR 0026 §16, issue #36).
/// Each entry binds one device to one scenario; multiple devices on the
/// same FakeBackend may each have a distinct scenario active at the same
/// time. v0.1.x — v0.2.3 stored a single global <c>ActiveScenario</c>
/// here; the migration to the per-device shape promotes that field
/// to the binding for the then-<see cref="CurrentDevice"/> at first
/// load (and drops it silently when no current device was set, since
/// there's no device to bind it to in the new model).
/// </param>
public sealed record SessionState(
    DeviceName? CurrentDevice,
    ImmutableDictionary<DeviceName, ScenarioName> DeviceScenarios
)
{
    /// <summary>The empty session state: no current device, no bindings.</summary>
    public static SessionState Empty { get; } =
        new(
            CurrentDevice: null,
            DeviceScenarios: ImmutableDictionary<DeviceName, ScenarioName>.Empty
        );

    /// <summary>
    /// Returns the active scenario bound to <paramref name="device"/>, or
    /// <see langword="null"/> when no binding exists.
    /// </summary>
    public ScenarioName? GetActiveScenario(DeviceName device) =>
        DeviceScenarios.TryGetValue(device, out var s) ? s : null;

    /// <summary>Returns a new state with <paramref name="scenario"/> bound to <paramref name="device"/>.</summary>
    public SessionState BindScenario(DeviceName device, ScenarioName scenario) =>
        this with
        {
            DeviceScenarios = DeviceScenarios.SetItem(device, scenario),
        };

    /// <summary>Returns a new state with any scenario binding for <paramref name="device"/> removed.</summary>
    public SessionState UnbindScenario(DeviceName device) =>
        DeviceScenarios.ContainsKey(device)
            ? this with
            {
                DeviceScenarios = DeviceScenarios.Remove(device),
            }
            : this;
}
