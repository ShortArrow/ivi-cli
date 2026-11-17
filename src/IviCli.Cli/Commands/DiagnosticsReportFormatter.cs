using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
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
    /// detail. Only quotes, backslashes and control characters are escaped;
    /// other text is written as it is.
    /// </summary>
    public static string FormatJson(DiagnosticsReport report)
    {
        using var stream = new MemoryStream();
        using (
            var json = new Utf8JsonWriter(
                stream,
                new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }
            )
        )
        {
            json.WriteStartObject();
            json.WriteStartArray("checks");
            foreach (var c in report.Checks)
            {
                json.WriteStartObject();
                json.WriteString("name", c.Name);
                json.WriteString("status", StatusLabel(c.Status));
                json.WriteString("detail", c.Detail);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string StatusLabel(DiagnosticStatus status) =>
        status switch
        {
            DiagnosticStatus.Ok => "ok",
            DiagnosticStatus.Warning => "warning",
            DiagnosticStatus.Error => "error",
            _ => "unknown",
        };
}
