using IviCli.Domain.Devices;
using IviCli.TestKit;

namespace IviCli.Domain.Tests.Devices;

public class DeviceNameTests
{
    [Theory]
    [InlineData("psu1")]
    [InlineData("scope1")]
    [InlineData("dmm1")]
    [InlineData("a")]
    [InlineData("a_b_c")]
    [InlineData("psu_001")]
    public void From_WithValidName_ReturnsOk(string raw)
    {
        // Given / When
        var result = DeviceName.From(raw);

        // Then
        result.ShouldBeOk().Value.ShouldBe(raw);
    }

    [Theory]
    [InlineData("")] // empty
    [InlineData("1psu")] // starts with digit
    [InlineData("Psu1")] // uppercase
    [InlineData("psu-1")] // hyphen
    [InlineData("psu 1")] // whitespace
    [InlineData("psu.1")] // dot
    [InlineData("_psu")] // starts with underscore
    public void From_WithInvalidFormat_ReturnsInvalidDeviceNameFormat(string raw)
    {
        // Given / When
        var result = DeviceName.From(raw);

        // Then
        var invalid = result.ShouldBeError().ShouldBeOfType<InvalidDeviceNameFormat>();
        invalid.Raw.ShouldBe(raw);
    }

    [Fact]
    public void From_WithLengthAboveLimit_ReturnsInvalidDeviceNameFormat()
    {
        // Given
        var raw = new string('a', 65);

        // When
        var result = DeviceName.From(raw);

        // Then
        result.ShouldBeError().ShouldBeOfType<InvalidDeviceNameFormat>().Raw.ShouldBe(raw);
    }

    [Fact]
    public void From_WithLengthAtLimit_ReturnsOk()
    {
        // Given
        var raw = new string('a', 64);

        // When / Then
        DeviceName.From(raw).ShouldBeOk().Value.ShouldBe(raw);
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        // Given
        var name = DeviceName.From("psu1").ShouldBeOk();

        // When / Then
        name.ToString().ShouldBe("psu1");
    }

    [Fact]
    public void Equality_IsByValue()
    {
        // Given
        var a = DeviceName.From("psu1").ShouldBeOk();
        var b = DeviceName.From("psu1").ShouldBeOk();
        var c = DeviceName.From("scope1").ShouldBeOk();

        // When / Then
        a.ShouldBe(b);
        a.ShouldNotBe(c);
    }
}
