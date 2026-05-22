using IviCli.Application.Devices;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.TestKit;

namespace IviCli.Application.Tests.Devices;

public class ListDevicesQueryHandlerTests
{
    private static Device Dev(string name, string resource = "TCPIP0::host::inst0::INSTR") =>
        new(
            DeviceName.From(name).ShouldBeOk(),
            VisaResource.Parse(resource).ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    [Fact]
    public async Task Handle_WithEmptyConfig_ReturnsEmptyListing()
    {
        // Given
        var handler = new ListDevicesQueryHandler(new FakeConfigStore());

        // When
        var result = await handler.HandleAsync(new ListDevicesQuery(), CancellationToken.None);

        // Then
        var listing = result.ShouldBeOk();
        listing.Devices.ShouldBeEmpty();
        listing.DefaultDevice.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WithDevices_ReturnsListingInInsertionOrder()
    {
        // Given
        var seeded = ConfigDocument
            .Empty.AddDevice(Dev("psu1"))
            .ShouldBeOk()
            .AddDevice(Dev("scope1", "USB0::0x0699::0x0408::SN::INSTR"))
            .ShouldBeOk();
        var handler = new ListDevicesQueryHandler(new FakeConfigStore(seeded));

        // When
        var result = await handler.HandleAsync(new ListDevicesQuery(), CancellationToken.None);

        // Then
        var listing = result.ShouldBeOk();
        listing.Devices.Length.ShouldBe(2);
        listing.Devices[0].Name.Value.ShouldBe("psu1");
        listing.Devices[1].Name.Value.ShouldBe("scope1");
    }

    [Fact]
    public async Task Handle_WithDefaultDevice_ReportsItInListing()
    {
        // Given
        var seeded = ConfigDocument
            .Empty.AddDevice(Dev("psu1"))
            .ShouldBeOk()
            .SetDefaultDevice(DeviceName.From("psu1").ShouldBeOk())
            .ShouldBeOk();
        var handler = new ListDevicesQueryHandler(new FakeConfigStore(seeded));

        // When
        var result = await handler.HandleAsync(new ListDevicesQuery(), CancellationToken.None);

        // Then
        var listing = result.ShouldBeOk();
        listing.DefaultDevice.ShouldNotBeNull();
        listing.DefaultDevice!.Value.ShouldBe("psu1");
    }

    [Fact]
    public async Task Handle_WhenStoreLoadFails_ReturnsListDevicesStorageFailure()
    {
        // Given
        var store = new FakeConfigStore();
        store.FailNextLoadWith("disk gone");
        var handler = new ListDevicesQueryHandler(store);

        // When
        var result = await handler.HandleAsync(new ListDevicesQuery(), CancellationToken.None);

        // Then
        var err = result.ShouldBeError().ShouldBeOfType<ListDevicesStorageFailure>();
        err.Inner.ShouldBeOfType<Application.Configuration.ConfigStoreReadFailure>();
    }
}
