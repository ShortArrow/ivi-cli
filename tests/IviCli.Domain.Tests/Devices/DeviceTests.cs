using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.TestKit;

namespace IviCli.Domain.Tests.Devices;

public class DeviceTests
{
    private static DeviceName Name(string raw) => DeviceName.From(raw).ShouldBeOk();

    private static VisaResource Resource(string raw) => VisaResource.Parse(raw).ShouldBeOk();

    private static Timeout T(int ms) => Timeout.FromMilliseconds(ms).ShouldBeOk();

    [Fact]
    public void Construction_StoresAllFields()
    {
        // Given
        var name = Name("psu1");
        var resource = Resource("TCPIP0::192.168.0.10::inst0::INSTR");
        var timeout = T(3000);

        // When
        var device = new Device(name, resource, timeout);

        // Then
        device.Name.ShouldBe(name);
        device.Resource.ShouldBe(resource);
        device.Timeout.ShouldBe(timeout);
    }

    [Fact]
    public void Equality_WithIdenticalFields_IsTrue()
    {
        // Given
        var a = new Device(Name("psu1"), Resource("TCPIP0::host::inst0::INSTR"), T(3000));
        var b = new Device(Name("psu1"), Resource("TCPIP0::host::inst0::INSTR"), T(3000));

        // When / Then
        a.ShouldBe(b);
    }

    [Fact]
    public void Equality_DifferingName_IsFalse()
    {
        // Given
        var a = new Device(Name("psu1"), Resource("TCPIP0::host::inst0::INSTR"), T(3000));
        var b = new Device(Name("psu2"), Resource("TCPIP0::host::inst0::INSTR"), T(3000));

        // When / Then
        a.ShouldNotBe(b);
    }

    [Fact]
    public void Equality_DifferingResource_IsFalse()
    {
        // Given
        var a = new Device(Name("psu1"), Resource("TCPIP0::host::inst0::INSTR"), T(3000));
        var b = new Device(Name("psu1"), Resource("TCPIP0::other::inst0::INSTR"), T(3000));

        // When / Then
        a.ShouldNotBe(b);
    }

    [Fact]
    public void Equality_DifferingTimeout_IsFalse()
    {
        // Given
        var a = new Device(Name("psu1"), Resource("TCPIP0::host::inst0::INSTR"), T(3000));
        var b = new Device(Name("psu1"), Resource("TCPIP0::host::inst0::INSTR"), T(5000));

        // When / Then
        a.ShouldNotBe(b);
    }

    [Fact]
    public void WithExpression_UpdatesTimeoutWithoutMutatingOriginal()
    {
        // Given
        var original = new Device(Name("psu1"), Resource("TCPIP0::host::inst0::INSTR"), T(3000));

        // When
        var updated = original with
        {
            Timeout = T(5000),
        };

        // Then
        original.Timeout.ShouldBe(T(3000));
        updated.Timeout.ShouldBe(T(5000));
        updated.Name.ShouldBe(original.Name);
        updated.Resource.ShouldBe(original.Resource);
    }
}
