namespace IviCli.Domain.Devices;

/// <summary>The parity a serial line checks.</summary>
public enum SerialParity
{
    /// <summary>No parity bit.</summary>
    None,

    /// <summary>Odd parity.</summary>
    Odd,

    /// <summary>Even parity.</summary>
    Even,

    /// <summary>The parity bit is always 1.</summary>
    Mark,

    /// <summary>The parity bit is always 0.</summary>
    Space,
}

/// <summary>The stop bits that end each character on a serial line.</summary>
public enum SerialStopBits
{
    /// <summary>One stop bit.</summary>
    One,

    /// <summary>One and a half stop bits.</summary>
    OnePointFive,

    /// <summary>Two stop bits.</summary>
    Two,
}

/// <summary>The flow control a serial line uses.</summary>
public enum SerialFlowControl
{
    /// <summary>No flow control.</summary>
    None,

    /// <summary>Software flow control with XON and XOFF characters.</summary>
    XonXoff,

    /// <summary>Hardware flow control with the RTS and CTS lines.</summary>
    RtsCts,
}

/// <summary>
/// How an ASRL device's serial line is set up (ADR 0055 §4). The defaults
/// are VISA's: 9600 baud, 8 data bits, no parity, one stop bit, no flow
/// control.
/// </summary>
public sealed record SerialSettings
{
    /// <summary>The least number of data bits a character carries.</summary>
    public const int MinDataBits = 5;

    /// <summary>The most data bits a character carries.</summary>
    public const int MaxDataBits = 8;

    /// <summary>VISA's defaults, 9600 8-N-1 without flow control.</summary>
    public static SerialSettings Default { get; } =
        new(9600, 8, SerialParity.None, SerialStopBits.One, SerialFlowControl.None);

    /// <summary>The baud rate; always positive.</summary>
    public int Baud { get; }

    /// <summary>
    /// The data bits per character, <see cref="MinDataBits"/> to
    /// <see cref="MaxDataBits"/>.
    /// </summary>
    public int DataBits { get; }

    /// <summary>The parity.</summary>
    public SerialParity Parity { get; }

    /// <summary>The stop bits.</summary>
    public SerialStopBits StopBits { get; }

    /// <summary>The flow control.</summary>
    public SerialFlowControl FlowControl { get; }

    private SerialSettings(
        int baud,
        int dataBits,
        SerialParity parity,
        SerialStopBits stopBits,
        SerialFlowControl flowControl
    )
    {
        Baud = baud;
        DataBits = dataBits;
        Parity = parity;
        StopBits = stopBits;
        FlowControl = flowControl;
    }

    /// <summary>Validates and constructs serial settings.</summary>
    /// <returns>
    /// The settings, <see cref="InvalidBaudRate"/> for a baud rate that is not
    /// positive, or <see cref="InvalidDataBits"/> for data bits out of range.
    /// </returns>
    public static Result<SerialSettings, DeviceError> From(
        int baud,
        int dataBits,
        SerialParity parity,
        SerialStopBits stopBits,
        SerialFlowControl flowControl
    )
    {
        if (baud <= 0)
        {
            return Result.Failure<SerialSettings, DeviceError>(new InvalidBaudRate(baud));
        }
        if (dataBits is < MinDataBits or > MaxDataBits)
        {
            return Result.Failure<SerialSettings, DeviceError>(new InvalidDataBits(dataBits));
        }
        return Result.Success<SerialSettings, DeviceError>(
            new SerialSettings(baud, dataBits, parity, stopBits, flowControl)
        );
    }
}
