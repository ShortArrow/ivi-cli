using IviCli.Application.Backends;
using IviCli.Application.Devices;
using IviCli.Backends.Fake;
using IviCli.Domain.Visa;
using IviCli.TestKit;

namespace IviCli.Application.Tests.Devices;

public class ScanDevicesQueryHandlerTests
{
    [Fact]
    public async Task Handle_WithNoScanners_ReturnsEmpty()
    {
        // Given
        var handler = new ScanDevicesQueryHandler(Array.Empty<IBackendScanner>());

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
        var handler = new ScanDevicesQueryHandler(new IBackendScanner[] { s1, s2 });

        // When
        var result = await handler.HandleAsync(new ScanDevicesQuery(), CancellationToken.None);

        // Then
        var scan = result.ShouldBeOk();
        scan.Resources.Length.ShouldBe(2);
    }

    [Fact]
    public async Task Handle_WhenAllScannersFail_PropagatesFailure()
    {
        // Given
        var scanner = new FakeBackendScanner().FailNextWith(new TransportDisconnected("offline"));
        var handler = new ScanDevicesQueryHandler(new IBackendScanner[] { scanner });

        // When
        var result = await handler.HandleAsync(new ScanDevicesQuery(), CancellationToken.None);

        // Then
        result.ShouldBeError().ShouldBeOfType<ScanDevicesScannerFailure>();
    }
}
