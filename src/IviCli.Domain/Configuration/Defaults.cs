using IviCli.Domain.Devices;

namespace IviCli.Domain.Configuration;

/// <summary>
/// The <c>[defaults]</c> section of a configuration document.
/// </summary>
/// <param name="Device">
/// The default device alias to operate on when no device is named at the CLI;
/// <see langword="null"/> when no default is set.
/// </param>
/// <remarks>
/// Server defaults will be added when Phase 2 introduces the gateway server.
/// </remarks>
public sealed record Defaults(DeviceName? Device)
{
    /// <summary>A <see cref="Defaults"/> with no values set.</summary>
    public static Defaults None { get; } = new(Device: null);
}
