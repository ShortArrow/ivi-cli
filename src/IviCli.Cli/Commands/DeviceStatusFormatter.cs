using System.Globalization;
using IviCli.Application.Devices;

namespace IviCli.Cli.Commands;

/// <summary>
/// Pure formatter for <see cref="DeviceStatus"/> output, so the
/// <c>visa status --json</c> contract can be locked down by tests without
/// spawning the CLI as a subprocess.
/// </summary>
public static class DeviceStatusFormatter
{
    /// <summary>
    /// Renders the status as the one-line JSON object emitted on stdout when
    /// <c>--json</c> is passed. The resource is the masked log form, so the
    /// output is safe to copy into a bug report.
    /// </summary>
    public static string FormatJson(DeviceStatus status)
    {
        var inv = CultureInfo.InvariantCulture;
        var resource = status.Device.Resource.ToLogString();
        var idnJson = status.IdnResponse is null ? "null" : $"\"{Escape(status.IdnResponse)}\"";
        var failureJson = status.FailureMessage is null
            ? "null"
            : $"\"{Escape(status.FailureMessage)}\"";
        return string.Create(
            inv,
            $"{{\"device\":\"{status.Device.Name.Value}\",\"resource\":\"{resource}\",\"online\":{(status.IsOnline ? "true" : "false")},\"response_time_ms\":{status.ResponseTime.TotalMilliseconds:F1},\"idn\":{idnJson},\"failure\":{failureJson}}}"
        );
    }

    private static string Escape(string raw) =>
        raw.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
