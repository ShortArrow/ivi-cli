using IviCli.Domain.Devices;

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
public sealed record SessionState(DeviceName? CurrentDevice)
{
    /// <summary>The empty session state: no current device.</summary>
    public static SessionState Empty { get; } = new(CurrentDevice: null);
}
