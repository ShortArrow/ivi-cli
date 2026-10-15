using IviCli.Domain.Devices;

namespace IviCli.Cli.Commands;

/// <summary>
/// The warning for a device whose terminator has no effect, because its
/// transport ends messages itself (ADR 0055 §5).
/// </summary>
internal static class TerminationWarning
{
    /// <summary>The warning line for <paramref name="device"/>.</summary>
    public static string For(Device device) =>
        $"warning: {device.Name.Value} sets a terminator, which its transport ignores: "
        + "HiSLIP, VXI-11, USBTMC and GPIB end messages themselves.";
}
