namespace IviCli.Api.Authentication;

/// <summary>
/// Maps Management API HTTP request paths to the PAT scope required
/// to invoke them (ADR 0044). The mapping is intentionally a small
/// static table — the API surface is fixed by ADR 0034 §2, so
/// reflection-based scope discovery would be over-engineering.
/// </summary>
public static class RoutePermissions
{
    /// <summary>Reading the device list / device status (ADR 0034 §2).</summary>
    public const string ReadDevices = "read:devices";

    /// <summary>Reading the configured gateway servers.</summary>
    public const string ReadServers = "read:servers";

    /// <summary>Reading the available mock scenarios.</summary>
    public const string ReadScenarios = "read:scenarios";

    /// <summary>Issuing SCPI write / query / WebSocket against a device.</summary>
    public const string WriteScpi = "write:scpi";

    /// <summary>
    /// Returns the scope required for <paramref name="method"/>
    /// <paramref name="path"/>, or <see langword="null"/> when the
    /// path needs no specific scope (bypass routes like
    /// <c>/healthz</c> or unrecognised routes — auth middleware
    /// short-circuits before this point).
    /// </summary>
    public static string? RequiredScope(string method, string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }
        // Match against the longest stable prefix first.
        if (
            path.StartsWith("/v1/devices/", StringComparison.OrdinalIgnoreCase)
            && (
                path.EndsWith("/query", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/write", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/ws", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            return WriteScpi;
        }
        if (
            path.StartsWith("/v1/devices", StringComparison.OrdinalIgnoreCase)
            && method.Equals("GET", StringComparison.OrdinalIgnoreCase)
        )
        {
            return ReadDevices;
        }
        if (
            path.StartsWith("/v1/servers", StringComparison.OrdinalIgnoreCase)
            && method.Equals("GET", StringComparison.OrdinalIgnoreCase)
        )
        {
            return ReadServers;
        }
        if (
            path.StartsWith("/v1/scenarios", StringComparison.OrdinalIgnoreCase)
            && method.Equals("GET", StringComparison.OrdinalIgnoreCase)
        )
        {
            return ReadScenarios;
        }
        return null;
    }
}
