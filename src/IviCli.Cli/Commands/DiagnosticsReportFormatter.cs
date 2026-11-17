using System.Globalization;
using System.Text;
using IviCli.Application.Diagnostics;

namespace IviCli.Cli.Commands;

/// <summary>
/// Pure formatter for <see cref="DiagnosticsReport"/> output, so the
/// <c>doctor --json</c> contract can be locked down by tests without
/// spawning the CLI as a subprocess.
/// </summary>
public static class DiagnosticsReportFormatter
{
    /// <summary>
    /// Renders the report as the one-line JSON object emitted on stdout when
    /// <c>--json</c> is passed: a <c>checks</c> array of name, status and
    /// detail.
    /// </summary>
    public static string FormatJson(DiagnosticsReport report)
    {
        var inv = CultureInfo.InvariantCulture;
        var builder = new StringBuilder("{\"checks\":[");
        for (var i = 0; i < report.Checks.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }
            var c = report.Checks[i];
            builder.Append(
                inv,
                $"{{\"name\":\"{c.Name}\",\"status\":\"{StatusLabel(c.Status)}\",\"detail\":\"{Escape(c.Detail)}\"}}"
            );
        }
        return builder.Append("]}").ToString();
    }

    private static string StatusLabel(DiagnosticStatus status) =>
        status switch
        {
            DiagnosticStatus.Ok => "ok",
            DiagnosticStatus.Warning => "warning",
            DiagnosticStatus.Error => "error",
            _ => "unknown",
        };

    private static string Escape(string raw) =>
        raw.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
