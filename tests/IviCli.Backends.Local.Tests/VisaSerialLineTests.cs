using System.Reflection;
using Ivi.Visa;
using IviCli.Backends.Local;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Backends.Local.Tests;

/// <summary>
/// A serial device opened through VISA gets its serial settings as
/// <see cref="ISerialSession"/> attributes, and DTR asserted (ADR 0055 §4).
/// </summary>
public sealed class VisaSerialLineTests
{
    [Fact]
    public void The_defaults_are_set_and_DTR_is_asserted_last()
    {
        var (session, recorder) = RecordingSerialSession.Create();

        VisaSerialLine.Configure(session, SerialSettings.Default).ShouldBeOk();

        recorder.Sets.ShouldBe([
            ("BaudRate", (object)9600),
            ("DataBits", (short)8),
            ("Parity", Ivi.Visa.SerialParity.None),
            ("StopBits", SerialStopBitsMode.One),
            ("FlowControl", SerialFlowControlModes.None),
            ("DataTerminalReadyState", LineState.Asserted),
        ]);
    }

    public static TheoryData<
        Domain.Devices.SerialParity,
        SerialStopBits,
        SerialFlowControl,
        Ivi.Visa.SerialParity,
        SerialStopBitsMode,
        SerialFlowControlModes
    > Mappings =>
        new()
        {
            {
                Domain.Devices.SerialParity.Odd,
                SerialStopBits.OnePointFive,
                SerialFlowControl.XonXoff,
                Ivi.Visa.SerialParity.Odd,
                SerialStopBitsMode.OneAndOneHalf,
                SerialFlowControlModes.XOnXOff
            },
            {
                Domain.Devices.SerialParity.Even,
                SerialStopBits.Two,
                SerialFlowControl.RtsCts,
                Ivi.Visa.SerialParity.Even,
                SerialStopBitsMode.Two,
                SerialFlowControlModes.RtsCts
            },
            {
                Domain.Devices.SerialParity.Mark,
                SerialStopBits.One,
                SerialFlowControl.None,
                Ivi.Visa.SerialParity.Mark,
                SerialStopBitsMode.One,
                SerialFlowControlModes.None
            },
            {
                Domain.Devices.SerialParity.Space,
                SerialStopBits.One,
                SerialFlowControl.None,
                Ivi.Visa.SerialParity.Space,
                SerialStopBitsMode.One,
                SerialFlowControlModes.None
            },
        };

    [Theory]
    [MemberData(nameof(Mappings))]
    public void Each_setting_maps_to_its_VISA_value(
        Domain.Devices.SerialParity parity,
        SerialStopBits stopBits,
        SerialFlowControl flowControl,
        Ivi.Visa.SerialParity visaParity,
        SerialStopBitsMode visaStopBits,
        SerialFlowControlModes visaFlowControl
    )
    {
        var settings = SerialSettings.From(19200, 7, parity, stopBits, flowControl).ShouldBeOk();
        var (session, recorder) = RecordingSerialSession.Create();

        VisaSerialLine.Configure(session, settings).ShouldBeOk();

        recorder.Sets.ShouldBe([
            ("BaudRate", (object)19200),
            ("DataBits", (short)7),
            ("Parity", visaParity),
            ("StopBits", visaStopBits),
            ("FlowControl", visaFlowControl),
            ("DataTerminalReadyState", LineState.Asserted),
        ]);
    }

    /// <summary>
    /// A setting the driver refuses fails the open and is named by its
    /// configuration name; nothing after it is set.
    /// </summary>
    [Theory]
    [InlineData("BaudRate", "baud")]
    [InlineData("DataBits", "data_bits")]
    [InlineData("Parity", "parity")]
    [InlineData("StopBits", "stop_bits")]
    [InlineData("FlowControl", "flow_control")]
    public void A_refused_setting_is_named(string property, string setting)
    {
        var (session, recorder) = RecordingSerialSession.Create();
        recorder.Refuses = property;

        var refused = VisaSerialLine
            .Configure(session, SerialSettings.Default)
            .ShouldBeError()
            .ShouldBeOfType<LocalVisaSettingRefused>();

        refused.Setting.ShouldBe(setting);
        refused.Cause.ShouldBeOfType<NativeVisaException>();
        recorder.Sets.Select(s => s.Property).ShouldNotContain("DataTerminalReadyState");
    }

    /// <summary>A port without modem lines keeps working without DTR, as on the Serial backend.</summary>
    [Fact]
    public void A_port_that_cannot_raise_DTR_is_used_without_it()
    {
        var (session, recorder) = RecordingSerialSession.Create();
        recorder.Refuses = "DataTerminalReadyState";

        VisaSerialLine.Configure(session, SerialSettings.Default).ShouldBeOk();
    }
}

/// <summary>
/// An <see cref="ISerialSession"/> that records the properties set on it
/// and whether it was disposed, and refuses one property, as a serial
/// driver refuses a value it cannot set, or fails on one otherwise.
/// </summary>
public class RecordingSerialSession : DispatchProxy
{
    public List<(string Property, object? Value)> Sets { get; } = [];

    /// <summary>The property whose setter throws, as the runtime does.</summary>
    public string? Refuses { get; set; }

    /// <summary>The property whose setter throws something the runtime does not.</summary>
    public string? Fails { get; set; }

    public bool Disposed { get; private set; }

    public static (ISerialSession Session, RecordingSerialSession Recorder) Create()
    {
        var session = Create<ISerialSession, RecordingSerialSession>();
        return (session, (RecordingSerialSession)(object)session);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        if (targetMethod.Name == nameof(IDisposable.Dispose))
        {
            Disposed = true;
            return null;
        }
        if (!targetMethod.Name.StartsWith("set_", StringComparison.Ordinal))
        {
            throw new NotSupportedException(targetMethod.Name);
        }
        var property = targetMethod.Name["set_".Length..];
        if (property == Refuses)
        {
            throw new NativeVisaException(NativeErrorCode.UnsupportedAttributeValue);
        }
        if (property == Fails)
        {
            throw new InvalidOperationException(property);
        }
        Sets.Add((property, args![0]));
        return null;
    }
}
