namespace IviCli.Domain.Devices;

/// <summary>
/// The names <c>config.toml</c> and <c>visa add</c> give the serial
/// settings (ADR 0055 §4): parity <c>none</c>, <c>odd</c>, <c>even</c>,
/// <c>mark</c> or <c>space</c>; stop bits <c>1</c>, <c>1.5</c> or <c>2</c>;
/// flow control <c>none</c>, <c>xon_xoff</c> or <c>rts_cts</c>.
/// </summary>
public static class SerialSettingNames
{
    /// <summary>The parity <paramref name="name"/> names, or <see langword="null"/>.</summary>
    public static SerialParity? ParseParity(string name) =>
        name switch
        {
            "none" => SerialParity.None,
            "odd" => SerialParity.Odd,
            "even" => SerialParity.Even,
            "mark" => SerialParity.Mark,
            "space" => SerialParity.Space,
            _ => null,
        };

    /// <summary>The stop bits <paramref name="name"/> names, or <see langword="null"/>.</summary>
    public static SerialStopBits? ParseStopBits(string name) =>
        name switch
        {
            "1" => SerialStopBits.One,
            "1.5" => SerialStopBits.OnePointFive,
            "2" => SerialStopBits.Two,
            _ => null,
        };

    /// <summary>The flow control <paramref name="name"/> names, or <see langword="null"/>.</summary>
    public static SerialFlowControl? ParseFlowControl(string name) =>
        name switch
        {
            "none" => SerialFlowControl.None,
            "xon_xoff" => SerialFlowControl.XonXoff,
            "rts_cts" => SerialFlowControl.RtsCts,
            _ => null,
        };

    /// <summary>The name of <paramref name="parity"/>.</summary>
    public static string Format(SerialParity parity) =>
        parity switch
        {
            SerialParity.None => "none",
            SerialParity.Odd => "odd",
            SerialParity.Even => "even",
            SerialParity.Mark => "mark",
            SerialParity.Space => "space",
            _ => throw new ArgumentOutOfRangeException(nameof(parity), parity, null),
        };

    /// <summary>The name of <paramref name="stopBits"/>.</summary>
    public static string Format(SerialStopBits stopBits) =>
        stopBits switch
        {
            SerialStopBits.One => "1",
            SerialStopBits.OnePointFive => "1.5",
            SerialStopBits.Two => "2",
            _ => throw new ArgumentOutOfRangeException(nameof(stopBits), stopBits, null),
        };

    /// <summary>The name of <paramref name="flowControl"/>.</summary>
    public static string Format(SerialFlowControl flowControl) =>
        flowControl switch
        {
            SerialFlowControl.None => "none",
            SerialFlowControl.XonXoff => "xon_xoff",
            SerialFlowControl.RtsCts => "rts_cts",
            _ => throw new ArgumentOutOfRangeException(nameof(flowControl), flowControl, null),
        };
}
