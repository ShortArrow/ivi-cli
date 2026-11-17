using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
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
    /// output is safe to copy into a bug report. The response time keeps one
    /// decimal place. Only quotes, backslashes and control characters are
    /// escaped; other text is written as it is.
    /// </summary>
    public static string FormatJson(DeviceStatus status)
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
            json.WriteString("device", status.Device.Name.Value);
            json.WriteString("resource", status.Device.Resource.ToLogString());
            json.WriteBoolean("online", status.IsOnline);
            json.WritePropertyName("response_time_ms");
            json.WriteRawValue(
                status.ResponseTime.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)
            );
            json.WriteString("idn", status.IdnResponse);
            json.WriteString("failure", status.FailureMessage);
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
