using IviCli.Domain.Visa;
using IviCli.TestKit;

namespace IviCli.Domain.Tests.Visa;

public class VisaResourceUsbTests
{
    [Fact]
    public void Parse_FullUsbResource_ReturnsUsb()
    {
        // Given
        const string raw = "USB0::0x0699::0x0408::C012345::INSTR";

        // When
        var result = VisaResource.Parse(raw);

        // Then
        var usb = result.ShouldBeOk().ShouldBeOfType<VisaResource.Usb>();
        usb.Board.ShouldBe(0);
        usb.VendorId.ShouldBe("0x0699");
        usb.ProductId.ShouldBe("0x0408");
        usb.SerialNumber.ShouldBe("C012345");
        usb.InterfaceNumber.ShouldBeNull();
    }

    [Fact]
    public void Parse_UsbWithImplicitBoard_DefaultsBoardToZero()
    {
        // Given
        const string raw = "USB::0x0699::0x0408::SN1::INSTR";

        // When
        var result = VisaResource.Parse(raw);

        // Then
        var usb = result.ShouldBeOk().ShouldBeOfType<VisaResource.Usb>();
        usb.Board.ShouldBe(0);
    }

    [Fact]
    public void Parse_UsbWithExplicitBoard_CapturesBoardNumber()
    {
        // Given
        const string raw = "USB2::0x0699::0x0408::SN1::INSTR";

        // When
        var result = VisaResource.Parse(raw);

        // Then
        var usb = result.ShouldBeOk().ShouldBeOfType<VisaResource.Usb>();
        usb.Board.ShouldBe(2);
    }

    [Fact]
    public void Parse_UsbWithInterfaceNumber_ReturnsUsbWithInterface()
    {
        // Given
        const string raw = "USB0::0x0699::0x0408::C012345::0::INSTR";

        // When
        var result = VisaResource.Parse(raw);

        // Then
        var usb = result.ShouldBeOk().ShouldBeOfType<VisaResource.Usb>();
        usb.InterfaceNumber.ShouldBe(0);
        usb.SerialNumber.ShouldBe("C012345");
    }

    [Theory]
    [InlineData("USB0::0X0699::0x0408::SN::INSTR")] // uppercase 0X prefix
    [InlineData("USB0::0x0699::0XABCD::SN::INSTR")] // uppercase hex digits
    public void Parse_UsbWithMixedCaseHex_NormalisesToLowercase(string raw)
    {
        // Given / When
        var result = VisaResource.Parse(raw);

        // Then
        var usb = result.ShouldBeOk().ShouldBeOfType<VisaResource.Usb>();
        usb.VendorId.ShouldStartWith("0x");
        usb.VendorId.ToLowerInvariant().ShouldBe(usb.VendorId);
        usb.ProductId.ShouldStartWith("0x");
        usb.ProductId.ToLowerInvariant().ShouldBe(usb.ProductId);
    }

    [Theory]
    [InlineData("USB0::0x0699::0x0408::INSTR")] // missing serial
    [InlineData("USB0::0x0699::SN::INSTR")] // missing product id
    [InlineData("USB0::not_hex::0x0408::SN::INSTR")] // non-hex vendor
    [InlineData("USB0::0x0699::not_hex::SN::INSTR")] // non-hex product
    [InlineData("USB0::0x069::0x0408::SN::INSTR")] // 3-digit vendor (must be 4)
    [InlineData("USB0::0x06990::0x0408::SN::INSTR")] // 5-digit vendor (must be 4)
    [InlineData("USB0::0x0699::0x0408::::INSTR")] // empty serial
    [InlineData("USBx::0x0699::0x0408::SN::INSTR")] // non-numeric board
    [InlineData("USB0::0x0699::0x0408::SN::INSTR::EXTRA")] // too many segments
    [InlineData("USB0::0x0699::0x0408::SN::NOTINSTR")] // wrong suffix
    [InlineData("USB0::0x0699::0x0408::SN::x::INSTR")] // non-numeric interface
    public void Parse_InvalidUsbInput_ReturnsInvalidVisaResourceFormat(string raw)
    {
        // Given / When
        var result = VisaResource.Parse(raw);

        // Then
        var err = result.ShouldBeError().ShouldBeOfType<InvalidVisaResourceFormat>();
        err.Raw.ShouldBe(raw);
    }

    [Fact]
    public void Usb_Equality_IsByValue()
    {
        // Given
        var a = VisaResource.Parse("USB0::0x0699::0x0408::SN1::INSTR").ShouldBeOk();
        var b = VisaResource.Parse("USB0::0x0699::0x0408::SN1::INSTR").ShouldBeOk();
        var c = VisaResource.Parse("USB0::0x0699::0x0408::SN2::INSTR").ShouldBeOk();

        // When / Then
        a.ShouldBe(b);
        a.ShouldNotBe(c);
    }
}
