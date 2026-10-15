using System.IO.Ports;
using System.Text;
using IviCli.Domain.Devices;

namespace IviCli.Backends.Serial;

/// <summary><see cref="ISerialLineFactory"/> over <see cref="SerialPort"/>.</summary>
public sealed class SerialPortLineFactory : ISerialLineFactory
{
    private const string WindowsDevicePrefix = @"\\.\";

    /// <summary>EBUSY, which Linux and macOS give for a port opened for exclusive use.</summary>
    private const int Busy = 16;

    /// <inheritdoc/>
    public ISerialLine Open(string port, SerialSettings settings, TimeSpan timeout)
    {
        var serialPort = new SerialPort();
        try
        {
            Configure(serialPort, port, settings, timeout);
            serialPort.Open();
            AssertDtr(serialPort);
            return new SerialPortLine(serialPort);
        }
        catch (IOException ex) when (!OperatingSystem.IsWindows() && ex.HResult == Busy)
        {
            // SerialPort opens a port for exclusive use, so a second open of
            // a port ivi-cli or another program holds that way fails busy.
            serialPort.Dispose();
            throw new UnauthorizedAccessException("the port is in use", ex);
        }
        catch
        {
            serialPort.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Asserts DTR, as VISA does by default (ADR 0055 §4), once the port is
    /// open. <c>SerialPort</c> fails its open when asked for DTR on a port
    /// with no modem lines, such as a pseudo-terminal, so DTR is raised
    /// here instead, and a port that has no DTR to raise keeps working.
    /// </summary>
    private static void AssertDtr(SerialPort serialPort)
    {
        try
        {
            serialPort.DtrEnable = true;
        }
        catch (IOException) { }
    }

    /// <summary>
    /// Sets <paramref name="serialPort"/> up for <paramref name="port"/>
    /// before it is opened: the serial settings, <paramref name="timeout"/>
    /// for reads and writes, and Latin-1 text so every byte passes unchanged
    /// (ADR 0055 §4, §8).
    /// </summary>
    public static void Configure(
        SerialPort serialPort,
        string port,
        SerialSettings settings,
        TimeSpan timeout
    )
    {
        ArgumentNullException.ThrowIfNull(serialPort);
        ArgumentNullException.ThrowIfNull(settings);
        serialPort.PortName = port.StartsWith(WindowsDevicePrefix, StringComparison.Ordinal)
            ? port[WindowsDevicePrefix.Length..]
            : port;
        serialPort.BaudRate = settings.Baud;
        serialPort.DataBits = settings.DataBits;
        serialPort.Parity = settings.Parity switch
        {
            SerialParity.None => Parity.None,
            SerialParity.Odd => Parity.Odd,
            SerialParity.Even => Parity.Even,
            SerialParity.Mark => Parity.Mark,
            SerialParity.Space => Parity.Space,
            _ => throw new ArgumentOutOfRangeException(nameof(settings), settings.Parity, null),
        };
        serialPort.StopBits = settings.StopBits switch
        {
            SerialStopBits.One => StopBits.One,
            SerialStopBits.OnePointFive => StopBits.OnePointFive,
            SerialStopBits.Two => StopBits.Two,
            _ => throw new ArgumentOutOfRangeException(nameof(settings), settings.StopBits, null),
        };
        serialPort.Handshake = settings.FlowControl switch
        {
            SerialFlowControl.None => Handshake.None,
            SerialFlowControl.XonXoff => Handshake.XOnXOff,
            SerialFlowControl.RtsCts => Handshake.RequestToSend,
            _ => throw new ArgumentOutOfRangeException(
                nameof(settings),
                settings.FlowControl,
                null
            ),
        };
        var milliseconds = (int)timeout.TotalMilliseconds;
        serialPort.ReadTimeout = milliseconds;
        serialPort.WriteTimeout = milliseconds;
        serialPort.Encoding = Encoding.Latin1;
    }

    private sealed class SerialPortLine(SerialPort port) : ISerialLine
    {
        public void Write(string text) => port.Write(text);

        public string ReadTo(string terminator) => port.ReadTo(terminator);

        public void Dispose() => port.Dispose();
    }
}
