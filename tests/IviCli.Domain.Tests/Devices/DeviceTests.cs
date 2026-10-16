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

    /// <summary>
    /// VISA's defaults have one representation, <see langword="null"/>, so a
    /// device that spells them out equals one that leaves them out.
    /// </summary>
    [Fact]
    public void Serial_DefaultSettings_AreStoredAsNone()
    {
        // Given
        var spelled = new Device(
            Name("dmm"),
            Resource("ASRL1::INSTR"),
            T(3000),
            Serial: SerialSettings.Default
        );
        var omitted = new Device(Name("dmm"), Resource("ASRL1::INSTR"), T(3000));

        // When
        var changed = omitted with
        {
            Serial = SerialSettings.Default,
        };

        // Then
        spelled.Serial.ShouldBeNull();
        changed.Serial.ShouldBeNull();
        spelled.ShouldBe(omitted);
    }

    /// <summary>
    /// HiSLIP, VXI-11, USBTMC and GPIB end messages themselves, so a
    /// terminator set on such a device has no effect (ADR 0055 §5).
    /// </summary>
    [Theory]
    [InlineData("TCPIP0::host::hislip0::INSTR", true)]
    [InlineData("TCPIP0::host::inst0::INSTR", true)]
    [InlineData("TCPIP0::host::INSTR", true)]
    [InlineData("USB0::0x0699::0x0408::C012345::INSTR", true)]
    [InlineData("GPIB0::5::INSTR", true)]
    [InlineData("TCPIP0::host::5025::SOCKET", false)]
    [InlineData("ASRL1::INSTR", false)]
    [InlineData("ASRL/dev/ttyUSB0::INSTR", false)]
    public void IgnoresTermination_WhenTheTransportEndsMessagesItself(string resource, bool ignored)
    {
        // Given
        var device = new Device(Name("d"), Resource(resource), T(3000)) with
        {
            ReadTermination = Termination.Cr,
        };

        // When / Then
        device.IgnoresTermination.ShouldBe(ignored);
    }

    [Fact]
    public void IgnoresTermination_IsFalseWithoutTerminators()
    {
        // Given
        var device = new Device(Name("d"), Resource("TCPIP0::host::hislip0::INSTR"), T(3000));

        // When / Then
        device.IgnoresTermination.ShouldBeFalse();
    }

    /// <summary>
    /// Only SOCKET and serial lack a message end of their own, so only
    /// there do the terminators apply (ADR 0055 §5).
    /// </summary>
    [Theory]
    [InlineData("TCPIP0::host::hislip0::INSTR", false)]
    [InlineData("TCPIP0::host::inst0::INSTR", false)]
    [InlineData("USB0::0x0699::0x0408::C012345::INSTR", false)]
    [InlineData("GPIB0::5::INSTR", false)]
    [InlineData("TCPIP0::host::5025::SOCKET", true)]
    [InlineData("ASRL1::INSTR", true)]
    [InlineData("ASRL/dev/ttyUSB0::INSTR", true)]
    public void UsesTermination_OnlyWhereTheTransportHasNoMessageEnd(string resource, bool uses)
    {
        // Given
        var device = new Device(Name("d"), Resource(resource), T(3000));

        // When / Then
        device.UsesTermination.ShouldBe(uses);
    }

    [Theory]
    [InlineData("TCPIP0::host::inst0::INSTR")]
    [InlineData("TCPIP0::host::5025::SOCKET")]
    public void SerialLine_IsNullOffASerialLine(string resource)
    {
        new Device(Name("d"), Resource(resource), T(3000)).SerialLine.ShouldBeNull();
    }

    [Fact]
    public void SerialLine_IsVisasDefaultsWhenTheDeviceSetsNone()
    {
        new Device(Name("d"), Resource("ASRL1::INSTR"), T(3000)).SerialLine.ShouldBe(
            SerialSettings.Default
        );
    }

    [Fact]
    public void SerialLine_IsTheDevicesSettings()
    {
        // Given
        var settings = SerialSettings
            .From(19200, 7, SerialParity.Even, SerialStopBits.Two, SerialFlowControl.RtsCts)
            .ShouldBeOk();

        // When
        var device = new Device(Name("d"), Resource("ASRLCOM3::INSTR"), T(3000), settings);

        // Then
        device.SerialLine.ShouldBe(settings);
    }
}
