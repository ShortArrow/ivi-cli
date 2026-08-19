using IviCli.Domain.Devices;

namespace IviCli.Cli.Commands;

/// <summary>
/// Phrases the rejection of a device name for the person who typed it: the
/// rule it broke, and — when folding case and punctuation reaches a valid
/// name — what to type instead.
/// </summary>
public static class DeviceNameMessage
{
    /// <summary>Returns the console line for a rejected device name.</summary>
    /// <param name="raw">The name as the user typed it.</param>
    public static string Invalid(string raw)
    {
        var suggestion = DeviceName.Suggest(raw);
        var hint = suggestion is null ? string.Empty : $" Try '{suggestion}'.";
        return $"error: invalid device name '{raw}': use {DeviceName.Requirement}.{hint}";
    }
}
