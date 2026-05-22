using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.TestKit;

namespace IviCli.Domain.Tests.Configuration;

public class ConfigDocumentTests
{
    private static DeviceName Name(string raw) => DeviceName.From(raw).ShouldBeOk();

    private static Device Dev(
        string name,
        string resource = "TCPIP0::host::inst0::INSTR",
        int timeoutMs = 3000
    ) =>
        new(
            Name(name),
            VisaResource.Parse(resource).ShouldBeOk(),
            Timeout.FromMilliseconds(timeoutMs).ShouldBeOk()
        );

    [Fact]
    public void Empty_HasNoDevicesAndNoDefaultDevice()
    {
        // Given / When
        var config = ConfigDocument.Empty;

        // Then
        config.Devices.ShouldBeEmpty();
        config.Defaults.Device.ShouldBeNull();
    }

    [Fact]
    public void AddDevice_WithUniqueName_AppendsDevice()
    {
        // Given
        var device = Dev("psu1");

        // When
        var result = ConfigDocument.Empty.AddDevice(device);

        // Then
        var updated = result.ShouldBeOk();
        updated.Devices.Length.ShouldBe(1);
        updated.Devices[0].ShouldBe(device);
    }

    [Fact]
    public void AddDevice_WithDuplicateName_ReturnsDuplicateDeviceName()
    {
        // Given
        var original = Dev("psu1");
        var clashing = Dev("psu1", resource: "USB0::0x0699::0x0408::SN::INSTR");
        var config = ConfigDocument.Empty.AddDevice(original).ShouldBeOk();

        // When
        var result = config.AddDevice(clashing);

        // Then
        var err = result.ShouldBeError().ShouldBeOfType<DuplicateDeviceName>();
        err.Name.ShouldBe(Name("psu1"));
    }

    [Fact]
    public void FindDevice_WhenPresent_ReturnsDevice()
    {
        // Given
        var device = Dev("psu1");
        var config = ConfigDocument.Empty.AddDevice(device).ShouldBeOk();

        // When
        var found = config.FindDevice(Name("psu1"));

        // Then
        found.ShouldBe(device);
    }

    [Fact]
    public void FindDevice_WhenAbsent_ReturnsNull()
    {
        // Given
        var config = ConfigDocument.Empty;

        // When
        var found = config.FindDevice(Name("ghost"));

        // Then
        found.ShouldBeNull();
    }

    [Fact]
    public void RemoveDevice_WhenPresent_RemovesIt()
    {
        // Given
        var config = ConfigDocument
            .Empty.AddDevice(Dev("psu1"))
            .ShouldBeOk()
            .AddDevice(Dev("scope1"))
            .ShouldBeOk();

        // When
        var result = config.RemoveDevice(Name("psu1"));

        // Then
        var updated = result.ShouldBeOk();
        updated.Devices.Length.ShouldBe(1);
        updated.Devices[0].Name.ShouldBe(Name("scope1"));
    }

    [Fact]
    public void RemoveDevice_WhenAbsent_ReturnsDeviceNotFound()
    {
        // Given
        var config = ConfigDocument.Empty;

        // When
        var result = config.RemoveDevice(Name("ghost"));

        // Then
        var err = result.ShouldBeError().ShouldBeOfType<DeviceNotFound>();
        err.Name.ShouldBe(Name("ghost"));
    }

    [Fact]
    public void RemoveDevice_WhenTargetIsDefault_AlsoClearsDefault()
    {
        // Given
        var config = ConfigDocument
            .Empty.AddDevice(Dev("psu1"))
            .ShouldBeOk()
            .SetDefaultDevice(Name("psu1"))
            .ShouldBeOk();

        // When
        var result = config.RemoveDevice(Name("psu1"));

        // Then
        var updated = result.ShouldBeOk();
        updated.Devices.ShouldBeEmpty();
        updated.Defaults.Device.ShouldBeNull();
    }

    [Fact]
    public void SetDefaultDevice_WithExistingDevice_Succeeds()
    {
        // Given
        var config = ConfigDocument.Empty.AddDevice(Dev("psu1")).ShouldBeOk();

        // When
        var result = config.SetDefaultDevice(Name("psu1"));

        // Then
        var updated = result.ShouldBeOk();
        updated.Defaults.Device.ShouldBe(Name("psu1"));
    }

    [Fact]
    public void SetDefaultDevice_WithMissingDevice_ReturnsDefaultDeviceMissing()
    {
        // Given
        var config = ConfigDocument.Empty;

        // When
        var result = config.SetDefaultDevice(Name("ghost"));

        // Then
        var err = result.ShouldBeError().ShouldBeOfType<DefaultDeviceMissing>();
        err.Name.ShouldBe(Name("ghost"));
    }

    [Fact]
    public void SetDefaultDevice_WithNull_ClearsDefault()
    {
        // Given
        var config = ConfigDocument
            .Empty.AddDevice(Dev("psu1"))
            .ShouldBeOk()
            .SetDefaultDevice(Name("psu1"))
            .ShouldBeOk();

        // When
        var result = config.SetDefaultDevice(null);

        // Then
        var updated = result.ShouldBeOk();
        updated.Defaults.Device.ShouldBeNull();
    }

    [Fact]
    public void Equality_IsByValue()
    {
        // Given
        var a = ConfigDocument
            .Empty.AddDevice(Dev("psu1"))
            .ShouldBeOk()
            .SetDefaultDevice(Name("psu1"))
            .ShouldBeOk();
        var b = ConfigDocument
            .Empty.AddDevice(Dev("psu1"))
            .ShouldBeOk()
            .SetDefaultDevice(Name("psu1"))
            .ShouldBeOk();

        // When / Then
        a.ShouldBe(b);
    }
}
