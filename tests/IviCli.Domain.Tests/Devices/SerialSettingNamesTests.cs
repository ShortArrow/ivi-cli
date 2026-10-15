using IviCli.Domain.Devices;

namespace IviCli.Domain.Tests.Devices;

/// <summary>
/// The names <c>config.toml</c> and <c>visa add</c> use for serial settings
/// (ADR 0055 §4).
/// </summary>
public class SerialSettingNamesTests
{
    [Theory]
    [InlineData("none", SerialParity.None)]
    [InlineData("odd", SerialParity.Odd)]
    [InlineData("even", SerialParity.Even)]
    [InlineData("mark", SerialParity.Mark)]
    [InlineData("space", SerialParity.Space)]
    public void Parity_RoundTrips(string name, SerialParity parity)
    {
        SerialSettingNames.ParseParity(name).ShouldBe(parity);
        SerialSettingNames.Format(parity).ShouldBe(name);
    }

    [Theory]
    [InlineData("1", SerialStopBits.One)]
    [InlineData("1.5", SerialStopBits.OnePointFive)]
    [InlineData("2", SerialStopBits.Two)]
    public void StopBits_RoundTrip(string name, SerialStopBits stopBits)
    {
        SerialSettingNames.ParseStopBits(name).ShouldBe(stopBits);
        SerialSettingNames.Format(stopBits).ShouldBe(name);
    }

    [Theory]
    [InlineData("none", SerialFlowControl.None)]
    [InlineData("xon_xoff", SerialFlowControl.XonXoff)]
    [InlineData("rts_cts", SerialFlowControl.RtsCts)]
    public void FlowControl_RoundTrips(string name, SerialFlowControl flowControl)
    {
        SerialSettingNames.ParseFlowControl(name).ShouldBe(flowControl);
        SerialSettingNames.Format(flowControl).ShouldBe(name);
    }

    [Theory]
    [InlineData("None")]
    [InlineData("sometimes")]
    [InlineData("")]
    public void ParseParity_UnknownName_ReturnsNull(string name) =>
        SerialSettingNames.ParseParity(name).ShouldBeNull();

    [Theory]
    [InlineData("3")]
    [InlineData("1.0")]
    [InlineData("one")]
    public void ParseStopBits_UnknownName_ReturnsNull(string name) =>
        SerialSettingNames.ParseStopBits(name).ShouldBeNull();

    [Theory]
    [InlineData("dtr_dsr")]
    [InlineData("XON_XOFF")]
    public void ParseFlowControl_UnknownName_ReturnsNull(string name) =>
        SerialSettingNames.ParseFlowControl(name).ShouldBeNull();
}
