using System.Collections.Immutable;
using IviCli.Application.Backends;
using IviCli.Application.Devices;
using IviCli.Backends.Fake;
using IviCli.Domain.Visa;
using IviCli.TestKit;

namespace IviCli.Application.Tests.Devices;

public class ScanDevicesQueryHandlerTests
{
    private static readonly FakeEndpointProber ClosedProber = new();

    [Fact]
    public async Task Handle_WithNoScanners_ReturnsEmpty()
    {
        // Given
        var handler = new ScanDevicesQueryHandler(Array.Empty<IBackendScanner>(), ClosedProber);

        // When
        var result = await handler.HandleAsync(new ScanDevicesQuery(), CancellationToken.None);

        // Then
        result.ShouldBeOk().Resources.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_AggregatesAllScannerResults()
    {
        // Given
        var s1 = new FakeBackendScanner().Register(
            VisaResource.Parse("TCPIP0::a::inst0::INSTR").ShouldBeOk(),
            "ACME,1"
        );
        var s2 = new FakeBackendScanner().Register(
            VisaResource.Parse("USB0::0x0699::0x0408::SN::INSTR").ShouldBeOk(),
            "TEKTRONIX,2"
        );
        var handler = new ScanDevicesQueryHandler(new IBackendScanner[] { s1, s2 }, ClosedProber);

        // When
        var result = await handler.HandleAsync(new ScanDevicesQuery(), CancellationToken.None);

        // Then — no enrichment ports open, so exactly the two discovered resources.
        var scan = result.ShouldBeOk();
        scan.Resources.Length.ShouldBe(2);
    }

    [Fact]
    public async Task Handle_WhenAllScannersFail_PropagatesFailure()
    {
        // Given
        var scanner = new FakeBackendScanner().FailNextWith(new TransportDisconnected("offline"));
        var handler = new ScanDevicesQueryHandler(new IBackendScanner[] { scanner }, ClosedProber);

        // When
        var result = await handler.HandleAsync(new ScanDevicesQuery(), CancellationToken.None);

        // Then
        result.ShouldBeError().ShouldBeOfType<ScanDevicesScannerFailure>();
    }

    [Fact]
    public async Task Handle_EnrichesDiscoveredHostWithHiSlipAndSocket()
    {
        // Given a host found only via VXI-11 (inst0) that also listens on
        // HiSLIP (4880) and SCPI-RAW (5025).
        var scanner = new FakeBackendScanner().Register(
            VisaResource.Parse("TCPIP0::192.168.3.100::inst0::INSTR").ShouldBeOk()
        );
        var prober = new FakeEndpointProber()
            .Open("192.168.3.100", 4880)
            .Open("192.168.3.100", 5025);
        var handler = new ScanDevicesQueryHandler(new IBackendScanner[] { scanner }, prober);

        // When
        var result = await handler.HandleAsync(new ScanDevicesQuery(), CancellationToken.None);

        // Then all three access paths are reported for the one host.
        var canonicals = result
            .ShouldBeOk()
            .Resources.Select(r => r.Resource.ToCanonical())
            .ToImmutableArray();
        canonicals.ShouldContain("TCPIP0::192.168.3.100::inst0::INSTR");
        canonicals.ShouldContain("TCPIP0::192.168.3.100::hislip0::INSTR");
        canonicals.ShouldContain("TCPIP0::192.168.3.100::5025::SOCKET");
    }

    [Fact]
    public async Task Handle_VerboseAttachesIdnFromSocketProbe()
    {
        // Given
        var scanner = new FakeBackendScanner().Register(
            VisaResource.Parse("TCPIP0::192.168.3.100::inst0::INSTR").ShouldBeOk()
        );
        var prober = new FakeEndpointProber().Open("192.168.3.100", 5025, "KIKUSUI,PWR801L,0,1.0");
        var handler = new ScanDevicesQueryHandler(new IBackendScanner[] { scanner }, prober);
        var query = new ScanDevicesQuery(
            new ScanOptions(ImmutableArray<int>.Empty, Subnet: null, Host: null, Verbose: true)
        );

        // When
        var result = await handler.HandleAsync(query, CancellationToken.None);

        // Then the SCPI-RAW resource carries the model from *IDN?.
        var socket = result
            .ShouldBeOk()
            .Resources.Single(r =>
                r.Resource.ToCanonical().EndsWith("::5025::SOCKET", StringComparison.Ordinal)
            );
        socket.Idn.ShouldBe("KIKUSUI,PWR801L,0,1.0");
    }
}
