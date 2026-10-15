using IviCli.Domain.Devices;

namespace IviCli.Backends.Serial;

/// <summary>
/// An open serial port, as the Serial backend uses it. Calls block, and a
/// read or write that runs past the timeout the port was opened with
/// throws <see cref="TimeoutException"/>.
/// </summary>
public interface ISerialLine : IDisposable
{
    /// <summary>Writes <paramref name="text"/>, terminator included.</summary>
    void Write(string text);

    /// <summary>
    /// Reads up to <paramref name="terminator"/> and returns what came
    /// before it.
    /// </summary>
    string ReadTo(string terminator);
}

/// <summary>Opens serial ports.</summary>
public interface ISerialLineFactory
{
    /// <summary>
    /// Opens <paramref name="port"/> with <paramref name="settings"/>, reads
    /// and writes bounded by <paramref name="timeout"/>.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The port is held by another program.</exception>
    /// <exception cref="IOException">The port does not exist or cannot be opened.</exception>
    /// <exception cref="ArgumentException">The platform cannot set one of <paramref name="settings"/>.</exception>
    ISerialLine Open(string port, SerialSettings settings, TimeSpan timeout);
}
