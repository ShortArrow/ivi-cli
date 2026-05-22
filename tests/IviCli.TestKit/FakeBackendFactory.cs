using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Devices;

namespace IviCli.TestKit;

/// <summary>
/// Trivial <see cref="IBackendFactory"/> for handler tests that hands back a
/// single supplied <see cref="IIviBackend"/> regardless of the device. Use
/// <see cref="DefaultBackendFactory"/> in Infrastructure tests when transport
/// dispatch itself is under test.
/// </summary>
public sealed class FakeBackendFactory : IBackendFactory
{
    private readonly IIviBackend _backend;

    /// <summary>Creates a factory bound to the supplied backend.</summary>
    public FakeBackendFactory(IIviBackend backend)
    {
        _backend = backend;
    }

    /// <inheritdoc/>
    public Result<IIviBackend, BackendError> CreateFor(Device device) =>
        Result.Success<IIviBackend, BackendError>(_backend);
}
