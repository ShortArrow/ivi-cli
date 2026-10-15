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
    /// <exception cref="SerialLineUnavailableException">The port is missing or may not be opened.</exception>
    /// <exception cref="IOException">The port cannot be opened for another reason.</exception>
    /// <exception cref="ArgumentException">The platform cannot set one of <paramref name="settings"/>.</exception>
    ISerialLine Open(string port, SerialSettings settings, TimeSpan timeout);
}

/// <summary>
/// A port that cannot be opened for a reason the operating system names,
/// such as a missing port or one the user may not open. The message says
/// why in words that never include the port's name, which logs mask
/// (ADR 0055 §1).
/// </summary>
public sealed class SerialLineUnavailableException : IOException
{
    /// <summary>Creates the exception with a reason free of the port's name.</summary>
    public SerialLineUnavailableException(string reason)
        : base(reason) { }

    /// <summary>Creates the exception with a reason and the exception it explains.</summary>
    public SerialLineUnavailableException(string reason, Exception inner)
        : base(reason, inner) { }

    /// <summary>Creates the exception with no reason.</summary>
    public SerialLineUnavailableException() { }
}
