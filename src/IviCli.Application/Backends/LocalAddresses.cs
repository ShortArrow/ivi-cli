using System.Net.NetworkInformation;

namespace IviCli.Application.Backends;

/// <summary>
/// Whether a scanner can send from a local address. The system will not
/// send from an address whose duplicate address detection failed, because
/// another host on the network uses it, or has not finished; binding to
/// one fails with <c>AddressNotAvailable</c>, and no instrument on that
/// network can be reached from this machine.
/// </summary>
public static class LocalAddresses
{
    /// <summary>
    /// The duplicate address detection state of <paramref name="address"/>,
    /// or <see langword="null"/> where the platform does not report it.
    /// Only Windows reports it.
    /// </summary>
    public static DuplicateAddressDetectionState? StateOf(UnicastIPAddressInformation address)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }
        try
        {
            return address.DuplicateAddressDetectionState;
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// True when the system sends from an address in <paramref name="state"/>.
    /// A state the platform does not report counts as usable.
    /// </summary>
    public static bool IsUsable(DuplicateAddressDetectionState? state) =>
        state
            is null
                or DuplicateAddressDetectionState.Preferred
                or DuplicateAddressDetectionState.Deprecated;
}
