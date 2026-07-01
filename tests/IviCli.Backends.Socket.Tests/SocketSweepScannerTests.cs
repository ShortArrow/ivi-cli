using IviCli.Application.Backends;
using IviCli.Backends.Socket;
using IviCli.Domain;
using IviCli.Domain.Visa;
using IviCli.TestKit;

namespace IviCli.Backends.Socket.Tests;

/// <summary>
/// Behavioural coverage for <see cref="SocketSweepScanner"/> driven by a fake
/// <see cref="IEndpointProber"/> and explicit <c>--host</c> / <c>--subnet</c>
/// targets — so no real NIC enumeration or socket traffic is involved.
/// </summary>
public sealed class SocketSweepScannerTests
{
    private static ScanOptions Ports(int[] ports, string? host = null, string? subnet = null) =>
        new([.. ports], subnet, host, Verbose: false);

    [Fact]
    public async Task Scan_without_sweep_ports_returns_empty()
    {
        var scanner = new SocketSweepScanner(new FakeEndpointProber().Open("192.168.3.110", 1394));

        var resources = await ScanAsync(scanner, ScanOptions.Default);

        resources.ShouldBeEmpty();
    }

    [Fact]
    public async Task Scan_host_reports_open_socket_port()
    {
        var prober = new FakeEndpointProber().Open("192.168.3.110", 1394);
        var scanner = new SocketSweepScanner(prober);

        var resources = await ScanAsync(scanner, Ports([1394], host: "192.168.3.110"));

        resources
            .Select(r => r.Resource.ToCanonical())
            .ShouldBe(["TCPIP0::192.168.3.110::1394::SOCKET"]);
    }

    [Fact]
    public async Task Scan_host_skips_closed_port()
    {
        var scanner = new SocketSweepScanner(new FakeEndpointProber());

        var resources = await ScanAsync(scanner, Ports([1394], host: "192.168.3.110"));

        resources.ShouldBeEmpty();
    }

    [Fact]
    public async Task Scan_subnet_probes_every_host_and_reports_responders()
    {
        var prober = new FakeEndpointProber().Open("192.168.3.1", 5025).Open("192.168.3.7", 5025);
        var scanner = new SocketSweepScanner(prober);

        var resources = await ScanAsync(scanner, Ports([5025], subnet: "192.168.3.0/24"));

        resources
            .Select(r => r.Resource.ToCanonical())
            .OrderBy(s => s, StringComparer.Ordinal)
            .ShouldBe(["TCPIP0::192.168.3.1::5025::SOCKET", "TCPIP0::192.168.3.7::5025::SOCKET"]);
    }

    [Fact]
    public async Task Scan_verbose_attaches_idn()
    {
        var prober = new FakeEndpointProber().Open(
            "192.168.3.110",
            1394,
            "KEITHLEY INSTRUMENTS INC.,MODEL 2701,0,1.0"
        );
        var scanner = new SocketSweepScanner(prober);
        var options = new ScanOptions([1394], Subnet: null, Host: "192.168.3.110", Verbose: true);

        var resources = await ScanAsync(scanner, options);

        resources.Single().Idn.ShouldBe("KEITHLEY INSTRUMENTS INC.,MODEL 2701,0,1.0");
    }

    [Fact]
    public async Task Scan_malformed_subnet_reports_nothing()
    {
        var scanner = new SocketSweepScanner(new FakeEndpointProber().Open("x", 5025));

        var resources = await ScanAsync(scanner, Ports([5025], subnet: "not-a-cidr"));

        resources.ShouldBeEmpty();
    }

    private static async Task<IReadOnlyList<DiscoveredResource>> ScanAsync(
        SocketSweepScanner scanner,
        ScanOptions options
    )
    {
        var result = await scanner.ScanAsync(options, CancellationToken.None);
        return result.ShouldBeOk();
    }
}
