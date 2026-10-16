using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;

namespace IviCli.Backends.Local;

/// <summary>
/// Port for opening VISA-compatible sessions against a resource string.
/// The production implementation calls the IVI Foundation VISA.NET shared
/// components, which locate an installed vendor implementation at runtime;
/// tests provide an in-memory fake. No vendor SDK is ever referenced.
/// </summary>
public interface IVisaSessionFactory
{
    /// <summary>
    /// Opens a VISA session to <paramref name="resource"/> and sets it up
    /// as <paramref name="options"/> says. The handle owns the underlying
    /// VISA session and must be disposed.
    /// </summary>
    /// <returns>
    /// The handle, or <see cref="LocalVisaSettingRefused"/> when the serial
    /// driver refuses one of the serial settings.
    /// </returns>
    Result<IVisaSessionHandle, LocalVisaError> Open(
        VisaResource resource,
        VisaSessionOptions options
    );
}

/// <summary>How a VISA session is opened and set up.</summary>
/// <param name="OpenTimeout">How long opening may take (viOpen's timeout).</param>
/// <param name="IoTimeout">The session's I/O timeout (VI_ATTR_TMO_VALUE).</param>
/// <param name="Serial">
/// The serial line's settings, for an ASRL resource only (ADR 0055 §4).
/// </param>
/// <param name="ReadTermination">
/// What ends a reply, for SOCKET and serial only; <see langword="null"/>
/// reads a line, which ends at LF or END, with a trailing CR removed
/// (ADR 0055 §5).
/// </param>
/// <param name="WriteTermination">
/// What ends a command, for SOCKET and serial only; <see langword="null"/>
/// means LF.
/// </param>
public sealed record VisaSessionOptions(
    TimeSpan OpenTimeout,
    TimeSpan IoTimeout,
    SerialSettings? Serial = null,
    Termination? ReadTermination = null,
    Termination? WriteTermination = null
);

/// <summary>
/// A line-oriented VISA session. Implementations wrap an
/// <c>IMessageBasedSession</c> from a vendor VISA runtime.
/// </summary>
public interface IVisaSessionHandle : IDisposable
{
    /// <summary>Writes <paramref name="text"/> as a single SCPI message.</summary>
    Result<Unit, LocalVisaError> Write(string text);

    /// <summary>Sends <paramref name="text"/> as a query and reads back the response.</summary>
    Result<string, LocalVisaError> Query(string text);

    /// <summary>Reads a single response message.</summary>
    Result<string, LocalVisaError> Read();

    /// <summary>
    /// Subscribes <paramref name="onStatusByte"/> to the instrument's
    /// service requests. After a successful call every service request the
    /// instrument raises invokes the callback exactly once with the status
    /// byte read back from the instrument. A single consumer is assumed —
    /// implementations need not support enabling twice. Disposing the
    /// handle tears the subscription down.
    /// </summary>
    Result<Unit, LocalVisaError> EnableServiceRequests(Action<byte> onStatusByte);
}
