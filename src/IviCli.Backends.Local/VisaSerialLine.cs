using Ivi.Visa;
using IviCli.Domain;
using IviCli.Domain.Devices;
using DomainParity = IviCli.Domain.Devices.SerialParity;
using VisaParity = Ivi.Visa.SerialParity;

namespace IviCli.Backends.Local;

/// <summary>Sets up a serial line opened through VISA (ADR 0055 §4).</summary>
public static class VisaSerialLine
{
    /// <summary>
    /// Sets <paramref name="settings"/> on <paramref name="session"/>, one
    /// attribute at a time, then asserts DTR, as the Serial backend does. A
    /// port with no DTR to raise is used without it.
    /// </summary>
    /// <returns>
    /// Success, or <see cref="LocalVisaSettingRefused"/> naming the first
    /// setting the driver refused; nothing after it is set.
    /// </returns>
    public static Result<Unit, LocalVisaError> Configure(
        ISerialSession session,
        SerialSettings settings
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(settings);
        var steps = new (string Setting, Action Set)[]
        {
            ("baud", () => session.BaudRate = settings.Baud),
            ("data_bits", () => session.DataBits = (short)settings.DataBits),
            ("parity", () => session.Parity = ToVisa(settings.Parity)),
            ("stop_bits", () => session.StopBits = ToVisa(settings.StopBits)),
            ("flow_control", () => session.FlowControl = ToVisa(settings.FlowControl)),
        };
        foreach (var (setting, set) in steps)
        {
            try
            {
                set();
            }
            catch (VisaException ex)
            {
                return Result.Failure<Unit, LocalVisaError>(
                    new LocalVisaSettingRefused(setting, ex)
                );
            }
        }
        try
        {
            session.DataTerminalReadyState = LineState.Asserted;
        }
        catch (VisaException) { }
        return Result.Success<Unit, LocalVisaError>(Unit.Value);
    }

    private static VisaParity ToVisa(DomainParity parity) =>
        parity switch
        {
            DomainParity.None => VisaParity.None,
            DomainParity.Odd => VisaParity.Odd,
            DomainParity.Even => VisaParity.Even,
            DomainParity.Mark => VisaParity.Mark,
            DomainParity.Space => VisaParity.Space,
            _ => throw new ArgumentOutOfRangeException(nameof(parity), parity, null),
        };

    private static SerialStopBitsMode ToVisa(SerialStopBits stopBits) =>
        stopBits switch
        {
            SerialStopBits.One => SerialStopBitsMode.One,
            SerialStopBits.OnePointFive => SerialStopBitsMode.OneAndOneHalf,
            SerialStopBits.Two => SerialStopBitsMode.Two,
            _ => throw new ArgumentOutOfRangeException(nameof(stopBits), stopBits, null),
        };

    private static SerialFlowControlModes ToVisa(SerialFlowControl flowControl) =>
        flowControl switch
        {
            SerialFlowControl.None => SerialFlowControlModes.None,
            SerialFlowControl.XonXoff => SerialFlowControlModes.XOnXOff,
            SerialFlowControl.RtsCts => SerialFlowControlModes.RtsCts,
            _ => throw new ArgumentOutOfRangeException(nameof(flowControl), flowControl, null),
        };
}
