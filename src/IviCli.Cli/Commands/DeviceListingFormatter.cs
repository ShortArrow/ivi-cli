using System.Globalization;
using System.Text;
using IviCli.Application.Devices;

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
            builder.Append('\n');
        }
        return builder.ToString();
    }

    /// <summary>
    /// Renders the listing as the JSON contract emitted on stdout when
    /// <c>--json</c> is passed. The shape is intentionally compact and
    /// stable; the snapshot test in <c>IviCli.Cli.Tests</c> locks it.
    /// </summary>
    public static string FormatJson(DeviceListing listing)
    {
        var inv = CultureInfo.InvariantCulture;
        var builder = new StringBuilder();
        builder.Append("{\"devices\":[");
        for (var i = 0; i < listing.Devices.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }
            var d = listing.Devices[i];
            builder.Append(
                inv,
                $"{{\"name\":\"{d.Name.Value}\",\"resource\":\"{d.Resource.ToCanonical()}\",\"timeout_ms\":{d.Timeout.Milliseconds}}}"
            );
        }
        builder.Append("],\"default\":");
        if (listing.DefaultDevice is { } def)
        {
            builder.Append(inv, $"\"{def.Value}\"");
        }
        else
        {
            builder.Append("null");
        }
        builder.Append("}\n");
        return builder.ToString();
    }
}
