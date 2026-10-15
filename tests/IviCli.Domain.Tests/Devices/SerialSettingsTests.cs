using IviCli.Domain.Devices;
using IviCli.TestKit;

namespace IviCli.Domain.Tests.Devices;

/// <summary>Serial settings default to VISA's 9600 8-N-1 without flow control (ADR 0055 §4).</summary>
public class SerialSettingsTests
{
    [Fact]
    public void Default_IsVisas()
    {
        // When
        var settings = SerialSettings.Default;

        // Then
        settings.Baud.ShouldBe(9600);
        settings.DataBits.ShouldBe(8);
        settings.Parity.ShouldBe(SerialParity.None);
        settings.StopBits.ShouldBe(SerialStopBits.One);
        settings.FlowControl.ShouldBe(SerialFlowControl.None);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    public void From_DataBitsInRange_ReturnsTheSettings(int dataBits)
    {
        // When
        var result = SerialSettings.From(
            115200,
            dataBits,
            SerialParity.Even,
            SerialStopBits.Two,
            SerialFlowControl.RtsCts
        );

        // Then
        var settings = result.ShouldBeOk();
        settings.Baud.ShouldBe(115200);
        settings.DataBits.ShouldBe(dataBits);
        settings.Parity.ShouldBe(SerialParity.Even);
        settings.StopBits.ShouldBe(SerialStopBits.Two);
        settings.FlowControl.ShouldBe(SerialFlowControl.RtsCts);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(9)]
    public void From_DataBitsOutOfRange_ReturnsInvalidDataBits(int dataBits)
    {
        // When
        var result = SerialSettings.From(
            9600,
            dataBits,
            SerialParity.None,
            SerialStopBits.One,
            SerialFlowControl.None
        );

        // Then
        result.ShouldBeError().ShouldBeOfType<InvalidDataBits>().Value.ShouldBe(dataBits);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-9600)]
    public void From_BaudNotPositive_ReturnsInvalidBaudRate(int baud)
    {
        // When
        var result = SerialSettings.From(
            baud,
            8,
            SerialParity.None,
            SerialStopBits.One,
            SerialFlowControl.None
        );

        // Then
        result.ShouldBeError().ShouldBeOfType<InvalidBaudRate>().Value.ShouldBe(baud);
    }
}
