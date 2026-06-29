using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using IviCli.Backends.Vxi11;
using Shouldly;

namespace IviCli.Backends.Vxi11.Tests;

/// <summary>
/// Unit coverage for the pure helpers behind the multi-NIC broadcast
/// scan: per-subnet directed-broadcast computation and the interface
/// filter. The socket round-trip itself is exercised against real
/// hardware (ADR 0008 §Verification), not in unit tests.
/// </summary>
public sealed class Vxi11BroadcastScannerTests
{
    [Theory]
    [InlineData("192.168.3.10", "255.255.255.0", "192.168.3.255")]
    [InlineData("10.0.0.5", "255.0.0.0", "10.255.255.255")]
    [InlineData("172.16.5.4", "255.255.0.0", "172.16.255.255")]
    [InlineData("192.168.1.130", "255.255.255.128", "192.168.1.255")]
    public void DirectedBroadcast_sets_host_bits_from_mask(
        string addr,
        string mask,
        string expected
    )
    {
        var result = Vxi11BroadcastScanner.DirectedBroadcast(
            IPAddress.Parse(addr),
            IPAddress.Parse(mask)
        );

        result.ShouldBe(IPAddress.Parse(expected));
    }

    [Fact]
    public void ShouldProbe_accepts_an_up_ipv4_ethernet_interface()
    {
        Vxi11BroadcastScanner
            .ShouldProbe(
                OperationalStatus.Up,
                NetworkInterfaceType.Ethernet,
                AddressFamily.InterNetwork,
                IPAddress.Parse("255.255.255.0")
            )
            .ShouldBeTrue();
    }

    [Theory]
    [InlineData(OperationalStatus.Down, NetworkInterfaceType.Ethernet, AddressFamily.InterNetwork)]
    [InlineData(OperationalStatus.Up, NetworkInterfaceType.Loopback, AddressFamily.InterNetwork)]
    [InlineData(OperationalStatus.Up, NetworkInterfaceType.Ethernet, AddressFamily.InterNetworkV6)]
    public void ShouldProbe_rejects_unusable_interfaces(
        OperationalStatus status,
        NetworkInterfaceType type,
        AddressFamily family
    )
    {
        Vxi11BroadcastScanner
            .ShouldProbe(status, type, family, IPAddress.Parse("255.255.255.0"))
            .ShouldBeFalse();
    }

    [Fact]
    public void ShouldProbe_rejects_a_missing_or_empty_mask()
    {
        Vxi11BroadcastScanner
            .ShouldProbe(
                OperationalStatus.Up,
                NetworkInterfaceType.Ethernet,
                AddressFamily.InterNetwork,
                null
            )
            .ShouldBeFalse();

        Vxi11BroadcastScanner
            .ShouldProbe(
                OperationalStatus.Up,
                NetworkInterfaceType.Ethernet,
                AddressFamily.InterNetwork,
                IPAddress.Any
            )
            .ShouldBeFalse();
    }
}
