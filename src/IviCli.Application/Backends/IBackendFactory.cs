using IviCli.Domain;
using IviCli.Domain.Devices;

namespace IviCli.Application.Backends;

/// <summary>
/// Selects the <see cref="IIviBackend"/> implementation appropriate for a
/// given <see cref="Device"/>'s VISA resource. The default implementation
/// lives in <c>IviCli.Infrastructure</c>; per ADR 0010 §4 it dispatches on
/// the resource variant (TCPIP / USB / GPIB / etc.).
/// </summary>
public interface IBackendFactory
{
    /// <summary>
    /// Resolves the Backend instance for <paramref name="device"/>.
    /// </summary>
    Result<IIviBackend, BackendError> CreateFor(Device device);
}
