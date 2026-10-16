using System.Globalization;
using System.Text;
using System.Text.Json;
using IviCli.Application.Devices;
using IviCli.Domain.Devices;

namespace IviCli.Cli.Commands;

/// <summary>
/// Pure formatters for <see cref="DeviceListing"/> output. Extracted from
/// <see cref="VisaListCommand"/> so the contract can be locked down with
/// snapshot tests (Verify, per ADR 0009 §7) without spawning the CLI as a
/// subprocess.
/// </summary>
public static class DeviceListingFormatter
{
    /// <summary>
    /// Renders the listing as the human-readable text table emitted on
    /// stdout when <c>--json</c> is not passed.
    /// </summary>
    public static string FormatHuman(DeviceListing listing)
    {
        if (listing.Devices.Length == 0)
        {
            return "(no devices configured)\n";
        }

        var builder = new StringBuilder();
        foreach (var d in listing.Devices)
        {
            var marker = listing.DefaultDevice == d.Name ? "*" : " ";
            builder.Append(marker);
            builder.Append(' ');
            builder.Append(d.Name.Value);
            builder.Append('\t');
            builder.Append(d.Resource.ToCanonical());
            builder.Append('\t');
            builder.Append(d.Timeout.ToString());
            if (d.SerialLine is { } line)
            {
                builder.Append('\t');
                builder.Append(FormatLine(line));
            }
            if (FormatTerminators(d) is { Length: > 0 } terminators)
            {
                builder.Append('\t');
                builder.Append(terminators);
            }
            builder.Append('\n');
        }
        return builder.ToString();
    }

    /// <summary>
    /// Renders the listing as the JSON contract emitted on stdout when
    /// <c>--json</c> is passed. The shape is intentionally compact and
    /// stable; the snapshot test in <c>IviCli.Cli.Tests</c> locks it. A
    /// serial device adds <c>serial</c>, the settings its line opens with,
    /// and a device adds each terminator it sets, all named as in
    /// <c>config.toml</c>.
    /// </summary>
    public static string FormatJson(DeviceListing listing)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteStartArray("devices");
            foreach (var d in listing.Devices)
            {
                WriteDevice(json, d);
            }
            json.WriteEndArray();
            if (listing.DefaultDevice is { } def)
            {
                json.WriteString("default", def.Value);
            }
            else
            {
                json.WriteNull("default");
            }
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static void WriteDevice(Utf8JsonWriter json, Device device)
    {
        json.WriteStartObject();
        json.WriteString("name", device.Name.Value);
        json.WriteString("resource", device.Resource.ToCanonical());
        json.WriteNumber("timeout_ms", device.Timeout.Milliseconds);
        if (device.SerialLine is { } line)
        {
            json.WriteStartObject("serial");
            json.WriteNumber("baud", line.Baud);
            json.WriteNumber("data_bits", line.DataBits);
            json.WriteString("parity", SerialSettingNames.Format(line.Parity));
            json.WritePropertyName("stop_bits");
            json.WriteRawValue(SerialSettingNames.Format(line.StopBits));
            json.WriteString("flow_control", SerialSettingNames.Format(line.FlowControl));
            json.WriteEndObject();
        }
        if (device.ReadTermination is { } read)
        {
            json.WriteString("read_termination", read.Value);
        }
        if (device.WriteTermination is { } write)
        {
            json.WriteString("write_termination", write.Value);
        }
        json.WriteEndObject();
    }

    /// <summary>The line as <c>9600 8-N-1</c>, with the flow control after it unless none.</summary>
    private static string FormatLine(SerialSettings line)
    {
        var parity = line.Parity switch
        {
            SerialParity.Odd => 'O',
            SerialParity.Even => 'E',
            SerialParity.Mark => 'M',
            SerialParity.Space => 'S',
            _ => 'N',
        };
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{line.Baud} {line.DataBits}-{parity}-{SerialSettingNames.Format(line.StopBits)}"
        );
        return line.FlowControl == SerialFlowControl.None
            ? text
            : $"{text} {SerialSettingNames.Format(line.FlowControl)}";
    }

    /// <summary>The terminators the device sets, as <c>read=\r write=\r\n</c>.</summary>
    private static string FormatTerminators(Device device)
    {
        var parts = new List<string>(2);
        if (device.ReadTermination is { } read)
        {
            parts.Add($"read={Escape(read)}");
        }
        if (device.WriteTermination is { } write)
        {
            parts.Add($"write={Escape(write)}");
        }
        return string.Join(' ', parts);
    }

    private static string Escape(Termination termination) =>
        termination
            .Value.Replace("\r", @"\r", StringComparison.Ordinal)
            .Replace("\n", @"\n", StringComparison.Ordinal);
}
