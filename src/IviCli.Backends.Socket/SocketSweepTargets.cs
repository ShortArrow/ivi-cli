using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace IviCli.Backends.Socket;

/// <summary>
/// Pure IPv4 subnet math and interface-selection predicates for the
/// <c>visa scan --port</c> TCP sweep (ADR 0008). Unlike VXI-11 portmapper
/// broadcast, a raw-SOCKET instrument (e.g. Keithley 2701 on port 1394)
/// has no discovery protocol, so the only way to find it is to open a TCP
/// connection to the target port on every host of the local subnet. These
/// helpers decide <em>which</em> addresses to probe; the socket round-trips
/// live in <c>SocketSweepScanner</c>.
/// </summary>
public static class SocketSweepTargets
{
    /// <summary>
    /// Parses <c>a.b.c.d/prefix</c> into its network address and prefix
    /// length. Returns <see langword="null"/> for malformed input, a prefix
    /// outside <c>0..32</c>, or a non-IPv4 address (IPv6 sweeps are out of
    /// scope). The returned network is the address with host bits cleared.
    /// </summary>
    public static (IPAddress Network, int PrefixLength)? TryParseCidr(string cidr)
    {
        var slash = cidr.IndexOf('/', StringComparison.Ordinal);
        if (slash < 0)
        {
            return null;
        }
        if (
            !IPAddress.TryParse(cidr[..slash], out var address)
            || address.AddressFamily != AddressFamily.InterNetwork
        )
        {
            return null;
        }
        if (!int.TryParse(cidr[(slash + 1)..], out var prefix) || prefix < 0 || prefix > 32)
        {
            return null;
        }
        return (NetworkAddress(address, prefix), prefix);
    }

    /// <summary>Counts the leading one-bits of an IPv4 subnet mask.</summary>
    public static int PrefixLength(IPAddress mask)
    {
        var bits = 0;
        foreach (var b in mask.GetAddressBytes())
        {
            bits += System.Numerics.BitOperations.PopCount(b);
        }
        return bits;
    }

    /// <summary>
    /// Enumerates the usable host addresses of the IPv4 subnet that
    /// <paramref name="address"/> belongs to under <paramref name="prefixLength"/>.
    /// For a routable prefix (&lt;= /30) the network and broadcast addresses are
    /// excluded; <c>/31</c> yields both addresses (RFC 3021 point-to-point) and
    /// <c>/32</c> the single host.
    /// </summary>
    public static IEnumerable<IPAddress> SubnetHosts(IPAddress address, int prefixLength)
    {
        var network = ToUInt32(NetworkAddress(address, prefixLength));
        var hostBits = 32 - prefixLength;
        var count = hostBits >= 32 ? 0x1_0000_0000UL : 1UL << hostBits;

        var (start, end) = prefixLength switch
        {
            32 => (0UL, 0UL), // single host
            31 => (0UL, 1UL), // both endpoints usable
            _ => (1UL, count - 2), // skip network (.0) and broadcast (last)
        };

        for (var offset = start; offset <= end; offset++)
        {
            yield return FromUInt32(network + (uint)offset);
        }
    }

    /// <summary>True when <paramref name="address"/> is IPv4 link-local (169.254.0.0/16).</summary>
    public static bool IsApipa(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b.Length == 4 && b[0] == 169 && b[1] == 254;
    }

    /// <summary>
    /// Decides whether an interface address should be TCP-swept: it must sit on
    /// an operational, non-loopback IPv4 interface, carry a usable mask whose
    /// prefix is at least <paramref name="minPrefixLength"/> (so the host count
    /// stays bounded — a /16 sweep is 65k probes), and not be APIPA link-local.
    /// </summary>
    public static bool ShouldSweep(
        OperationalStatus status,
        NetworkInterfaceType type,
        AddressFamily family,
        IPAddress address,
        IPAddress? mask,
        int minPrefixLength
    ) =>
        status == OperationalStatus.Up
        && type != NetworkInterfaceType.Loopback
        && family == AddressFamily.InterNetwork
        && mask is not null
        && !mask.Equals(IPAddress.Any)
        && !IsApipa(address)
        && PrefixLength(mask) >= minPrefixLength;

    private static IPAddress NetworkAddress(IPAddress address, int prefixLength)
    {
        var value = ToUInt32(address);
        var maskBits = prefixLength == 0 ? 0u : 0xFFFF_FFFFu << (32 - prefixLength);
        return FromUInt32(value & maskBits);
    }

    private static uint ToUInt32(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }

    private static IPAddress FromUInt32(uint value) =>
        new([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
}
