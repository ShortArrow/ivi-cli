using IviCli.Domain.Devices;

namespace IviCli.Application.Backends;

/// <summary>
/// No-op <see cref="IScenarioBindingRefresher"/> used when live re-binding
/// is not wired (e.g. a gateway constructed without the Fake backend's
/// refresher registered). Leaves the running binding exactly as seeded at
/// startup — the pre-feature behaviour — so a gateway stays fully functional
/// without the enhancement.
/// </summary>
public sealed class NullScenarioBindingRefresher : IScenarioBindingRefresher
{
    /// <summary>The shared stateless instance.</summary>
    public static readonly NullScenarioBindingRefresher Instance = new();

    private NullScenarioBindingRefresher() { }

    /// <inheritdoc/>
    public Task RefreshAsync(Device device, CancellationToken ct) => Task.CompletedTask;
}
