using System.IO.Ports;
using System.Text;
using IviCli.Domain.Devices;

namespace IviCli.Backends.Serial;

/// <summary><see cref="ISerialLineFactory"/> over <see cref="SerialPort"/>.</summary>
public sealed class SerialPortLineFactory : ISerialLineFactory
{
    private const string WindowsDevicePrefix = @"\\.\";

    /// <inheritdoc/>
    public ISerialLine Open(string port, SerialSettings settings, TimeSpan timeout)
    {
        var serialPort = new SerialPort();
        try
        {
            Configure(serialPort, port, settings, timeout);
            serialPort.Open();
            return new SerialPortLine(serialPort);
        }
        catch
        {
            serialPort.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Sets <paramref name="serialPort"/> up for <paramref name="port"/>:
    /// the serial settings, <paramref name="timeout"/> for reads and writes,
    /// DTR asserted as VISA does by default, and Latin-1 text so every byte
    /// passes unchanged (ADR 0055 §4, §8).
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
        serialPort.DtrEnable = true;
        serialPort.Encoding = Encoding.Latin1;
    }

    private sealed class SerialPortLine(SerialPort port) : ISerialLine
    {
        public void Write(string text) => port.Write(text);

        public string ReadTo(string terminator) => port.ReadTo(terminator);

        public void Dispose() => port.Dispose();
    }
}
