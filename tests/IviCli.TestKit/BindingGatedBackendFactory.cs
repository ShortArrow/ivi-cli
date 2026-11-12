using IviCli.Application.Backends;
using IviCli.Backends.Fake;
using IviCli.Domain;
using IviCli.Domain.Devices;

namespace IviCli.TestKit;

/// <summary>
/// <see cref="IBackendFactory"/> that returns the Fake backend only for a
/// device that backend holds a binding for, and otherwise fails as an
/// unbound <c>MOCK::INSTR</c> device does. Which backend a connection gets
/// therefore depends on the binding at the moment <see cref="CreateFor"/>
/// runs.
/// </summary>
public sealed class BindingGatedBackendFactory : IBackendFactory
{
    private readonly FakeBackend _fake;

    /// <summary>Creates a factory over the supplied backend.</summary>
    public BindingGatedBackendFactory(FakeBackend fake)
    {
        _fake = fake;
    }

    /// <inheritdoc/>
    public Result<IIviBackend, BackendError> CreateFor(Device device) =>
        _fake.HasActiveScenarioFor(device)
            ? Result.Success<IIviBackend, BackendError>(_fake)
            : Result.Failure<IIviBackend, BackendError>(new NoScenarioBound(device.Name));
}
