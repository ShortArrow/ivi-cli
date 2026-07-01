using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using IviCli.Backends.Socket;

namespace IviCli.Backends.Socket.Tests;

/// <summary>
/// Pure-function coverage for <see cref="SocketSweepTargets"/> — the CIDR /
/// subnet math and interface-selection predicate that decide which IPv4
/// addresses a <c>visa scan --port</c> TCP sweep probes. Socket round-trips
/// themselves are exercised by real-hardware / integration paths, not here.
/// </summary>
public sealed class SocketSweepTargetsTests
{
    [Theory]
    [InlineData("192.168.3.0/24", "192.168.3.0", 24)]
    [InlineData("10.0.0.0/8", "10.0.0.0", 8)]
    [InlineData("192.168.3.128/25", "192.168.3.128", 25)]
    [InlineData("192.168.3.10/32", "192.168.3.10", 32)]
    public void TryParseCidr_accepts_valid_cidr(string cidr, string network, int prefix)
    {
        var parsed = SocketSweepTargets.TryParseCidr(cidr);

        parsed.ShouldNotBeNull();
        parsed!.Value.Network.ShouldBe(IPAddress.Parse(network));
        parsed.Value.PrefixLength.ShouldBe(prefix);
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("192.168.3.0")] // missing prefix
    [InlineData("192.168.3.0/33")] // prefix out of range
    [InlineData("192.168.3.0/-1")]
    [InlineData("192.168.3.0/abc")]
    [InlineData("::1/64")] // IPv6 unsupported
    public void TryParseCidr_rejects_malformed_cidr(string cidr)
    {
        SocketSweepTargets.TryParseCidr(cidr).ShouldBeNull();
    }

    [Theory]
    [InlineData("255.255.255.0", 24)]
    [InlineData("255.255.0.0", 16)]
    [InlineData("255.255.255.128", 25)]
    [InlineData("255.255.255.252", 30)]
    [InlineData("0.0.0.0", 0)]
    [InlineData("255.255.255.255", 32)]
    public void PrefixLength_counts_contiguous_mask_bits(string mask, int expected)
    {
        SocketSweepTargets.PrefixLength(IPAddress.Parse(mask)).ShouldBe(expected);
    }

    [Fact]
    public void SubnetHosts_slash24_yields_254_usable_hosts()
    {
        var hosts = SocketSweepTargets.SubnetHosts(IPAddress.Parse("192.168.3.10"), 24).ToList();

        hosts.Count.ShouldBe(254);
        hosts.First().ShouldBe(IPAddress.Parse("192.168.3.1"));
        hosts.Last().ShouldBe(IPAddress.Parse("192.168.3.254"));
        hosts.ShouldNotContain(IPAddress.Parse("192.168.3.0")); // network
        hosts.ShouldNotContain(IPAddress.Parse("192.168.3.255")); // broadcast
    }

    [Fact]
    public void SubnetHosts_slash30_yields_two_hosts()
    {
        var hosts = SocketSweepTargets.SubnetHosts(IPAddress.Parse("192.168.3.5"), 30).ToList();

        hosts.ShouldBe(
            [IPAddress.Parse("192.168.3.5"), IPAddress.Parse("192.168.3.6")],
            ignoreOrder: false
        );
    }

    [Fact]
    public void SubnetHosts_slash32_yields_single_host()
    {
        SocketSweepTargets
            .SubnetHosts(IPAddress.Parse("192.168.3.10"), 32)
            .ShouldBe([IPAddress.Parse("192.168.3.10")]);
    }

    [Theory]
    [InlineData("169.254.7.8", true)]
    [InlineData("192.168.3.10", false)]
    [InlineData("10.0.0.1", false)]
    public void IsApipa_detects_link_local(string address, bool expected)
    {
        SocketSweepTargets.IsApipa(IPAddress.Parse(address)).ShouldBe(expected);
    }

    [Fact]
    public void ShouldSweep_accepts_operational_ipv4_slash24()
    {
        SocketSweepTargets
            .ShouldSweep(
                OperationalStatus.Up,
                NetworkInterfaceType.Ethernet,
                AddressFamily.InterNetwork,
                IPAddress.Parse("192.168.3.10"),
                IPAddress.Parse("255.255.255.0"),
                minPrefixLength: 24
            )
            .ShouldBeTrue();
    }

    [Theory]
    // Larger than /24 (fewer prefix bits → more hosts): skipped for safety.
    [InlineData(OperationalStatus.Up, NetworkInterfaceType.Ethernet, "192.168.0.10", "255.255.0.0")]
    // APIPA link-local: no reachable instrument subnet.
    [InlineData(OperationalStatus.Up, NetworkInterfaceType.Ethernet, "169.254.7.8", "255.255.0.0")]
    // Interface down.
    [InlineData(
        OperationalStatus.Down,
        NetworkInterfaceType.Ethernet,
        "192.168.3.10",
        "255.255.255.0"
    )]
    // Loopback.
    [InlineData(OperationalStatus.Up, NetworkInterfaceType.Loopback, "127.0.0.1", "255.0.0.0")]
    public void ShouldSweep_rejects_unsuitable_interfaces(
        OperationalStatus status,
        NetworkInterfaceType type,
        string address,
        string mask
    )
    {
        SocketSweepTargets
            .ShouldSweep(
                status,
                type,
                AddressFamily.InterNetwork,
                IPAddress.Parse(address),
                IPAddress.Parse(mask),
                minPrefixLength: 24
            )
            .ShouldBeFalse();
    }
}
