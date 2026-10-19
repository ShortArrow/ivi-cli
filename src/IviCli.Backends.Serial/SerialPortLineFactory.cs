using System.Globalization;
using System.IO.Ports;
using System.Text;
using IviCli.Domain.Devices;

namespace IviCli.Backends.Serial;

/// <summary><see cref="ISerialLineFactory"/> over <see cref="SerialPort"/>.</summary>
public sealed class SerialPortLineFactory : ISerialLineFactory
{
    private const string WindowsDevicePrefix = @"\\.\";

    private const int NoSuchPort = 2; // ENOENT on Linux and macOS
    private const int PermissionDenied = 13; // EACCES on Linux and macOS

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
        catch (Exception ex)
        {
            serialPort.Dispose();
            var translated = Translate(ex, OperatingSystem.IsWindows());
            if (ReferenceEquals(translated, ex))
            {
                throw;
            }
            throw translated;
        }
    }

    /// <summary>
    /// The exception a failed open stands for. On Linux and macOS
    /// <c>SerialPort</c> reports every failed open as access denied and keeps
    /// the errno in the inner exception: a missing port and one the user may
    /// not open become <see cref="SerialLineUnavailableException"/>, and the
    /// rest, a busy port among them, stay access denied, which the backend
    /// reports as in use. On Windows, access denied already means in use,
    /// and a missing port is a file-not-found error whose message names it.
    /// </summary>
    public static Exception Translate(Exception failure, bool windows)
    {
        ArgumentNullException.ThrowIfNull(failure);
        if (windows)
        {
            return failure is FileNotFoundException
                ? new SerialLineUnavailableException("the port does not exist", failure)
                : failure;
        }
        if (failure is not UnauthorizedAccessException { InnerException: { } inner })
        {
            return failure;
        }
        return inner.HResult switch
        {
            NoSuchPort => new SerialLineUnavailableException("the port does not exist", failure),
            PermissionDenied => new SerialLineUnavailableException(
                "permission denied; on Linux, add the user to the group that owns the port, usually dialout",
                failure
            ),
            _ => failure,
        };
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
    /// Whether <see cref="SerialPort"/> can open <paramref name="port"/> by
    /// its name. On Windows it opens only <c>COM</c> followed by a number,
    /// after a <c>\\.\</c> prefix is removed, though a driver can list
    /// other names, such as com0com's <c>CNCA0</c>. Elsewhere a port is a
    /// path, which its name alone cannot rule out.
    /// </summary>
    public static bool CanOpen(string port, bool onWindows)
    {
        ArgumentNullException.ThrowIfNull(port);
        if (!onWindows)
        {
            return true;
        }
        var name = PortName(port);
        return name.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(
                name.AsSpan(3),
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out _
            );
    }

    private static string PortName(string port) =>
        port.StartsWith(WindowsDevicePrefix, StringComparison.Ordinal)
            ? port[WindowsDevicePrefix.Length..]
            : port;

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
        serialPort.PortName = PortName(port);
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
        // SerialPort refuses 0; a device timeout of 0 means not to wait.
        var milliseconds = Math.Max(1, (int)timeout.TotalMilliseconds);
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
