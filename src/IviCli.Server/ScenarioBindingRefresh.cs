using IviCli.Application.Backends;
using IviCli.Domain.Devices;
using Microsoft.Extensions.Logging;

namespace IviCli.Server;

/// <summary>
/// How a gateway brings a device's scenario binding in line with the
/// session, so that a <c>mock scenario activate</c> or <c>deactivate</c> run
/// by another process reaches a serving gateway without a restart.
/// </summary>
internal static class ScenarioBindingRefresh
{
    /// <summary>
    /// Reconciles <paramref name="device"/>'s binding with the session. A
    /// failure is logged as a warning and not thrown, so a refresh never ends
    /// the connection it runs for; the binding then stays as it was.
    /// </summary>
    public static async Task RefreshOrWarnAsync(
        this IScenarioBindingRefresher refresher,
        Device device,
        ILogger logger,
        CancellationToken ct
    )
    {
        try
        {
            await refresher.RefreshAsync(device, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "scenario binding refresh failed; continuing");
        }
    }
}
