using IviCli.Domain.Devices;

namespace IviCli.Application.Backends;

/// <summary>
/// Re-syncs a device's active mock-scenario binding from the persisted
/// session into the running scenario-aware backend, so that a gateway
/// that is already serving picks up a later
/// <c>ivicli mock scenario activate</c> (performed by a separate
/// process, which only writes the session) without a restart.
///
/// The in-memory bindings on the backend are populated once at startup;
/// a subsequent <c>activate</c> in another process writes the session
/// but cannot touch this process's memory. Refreshing on each new
/// connection closes that gap.
///
/// Re-application is scoped: the binding is only re-applied when the
/// bound scenario *name* changed. An unchanged binding is left alone so
/// in-flight scene / transition state (e.g. an <c>on</c>/<c>off</c>
/// power scene the client toggled) is preserved across the frequent
/// reconnects the app performs.
/// </summary>
public interface IScenarioBindingRefresher
{
    /// <summary>
    /// Re-syncs <paramref name="device"/>'s scenario binding from the
    /// persisted session. Must be safe to call on every connection and
    /// must not throw; store errors are swallowed / logged.
    /// </summary>
    Task RefreshAsync(Device device, CancellationToken ct);
}
