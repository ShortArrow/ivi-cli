using IviCli.Domain.Visa;
using IviCli.TestKit;

namespace IviCli.Domain.Tests.Visa;

/// <summary>
/// Verifies the redaction contract declared in ADR 0017 §3: variable
/// sensitive segments (host, serial number) become <c>***</c> in
/// <see cref="VisaResource.ToLogString"/>.
/// </summary>
public class VisaResourceLoggingTests
{
    [Fact]
    public void Tcpip_ToLogString_MasksHost()
    {
        // Given
        var resource = VisaResource.Parse("TCPIP0::192.168.0.10::inst0::INSTR").ShouldBeOk();

        // When
        var masked = resource.ToLogString();

        // Then
        masked.ShouldNotContain("192.168.0.10");
        masked.ShouldContain("***");
        masked.ShouldStartWith("TCPIP0::");
        masked.ShouldEndWith("::inst0::INSTR");
    }

    [Fact]
    public void Usb_ToLogString_MasksSerialButKeepsVendorAndProduct()
    {
        // Given
        var resource = VisaResource.Parse("USB0::0x0699::0x0408::C012345::INSTR").ShouldBeOk();

        // When
        var masked = resource.ToLogString();

        // Then
        masked.ShouldNotContain("C012345");
        masked.ShouldContain("***");
        masked.ShouldContain("0x0699");
        masked.ShouldContain("0x0408");
    }

    [Fact]
    public void Usb_ToLogString_WithInterfaceNumber_PreservesInterface()
    {
        // Given
        var resource = VisaResource.Parse("USB0::0x0699::0x0408::SN::5::INSTR").ShouldBeOk();

        // When
        var masked = resource.ToLogString();

        // Then
        masked.ShouldNotContain("SN");
        masked.ShouldContain("***");
        masked.ShouldEndWith("::5::INSTR");
    }

    [Fact]
    public void Gpib_ToLogString_LeavesAddressVisible()
    {
        // Given — GPIB has no sensitive content; the addresses are deployment data.
        var resource = VisaResource.Parse("GPIB0::5::INSTR").ShouldBeOk();

        // When
        var masked = resource.ToLogString();

        // Then
        masked.ShouldBe("GPIB0::5::INSTR");
    }
}
