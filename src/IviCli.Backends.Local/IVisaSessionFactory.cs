using IviCli.Domain;
using IviCli.Domain.Visa;

namespace IviCli.Backends.Local;

/// <summary>
/// Port for opening VISA-compatible sessions against a resource string.
/// The production implementation reflectively loads the IVI VISA shared
/// component (NI-VISA / Keysight VISA) at runtime; tests provide an
/// in-memory fake. This keeps the LocalBackend project free of a
/// compile-time dependency on any vendor SDK.
/// </summary>
public interface IVisaSessionFactory
{
    /// <summary>
    /// Opens a VISA session to <paramref name="resource"/>. The handle
    /// owns the underlying VISA session and must be disposed.
    /// </summary>
    Result<IVisaSessionHandle, LocalVisaError> Open(VisaResource resource, TimeSpan timeout);
}

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
}
