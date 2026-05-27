using IviCli.Application.Backends;
using IviCli.Application.Capture;
using IviCli.Backends.Fake;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Application.Tests.Backends;

public sealed class CapturingBackendFactoryTests
{
    private static Device Dev(string name) =>
        new(
            DeviceName.From(name).ShouldBeOk(),
            VisaResource.Parse("TCPIP0::host::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    [Fact]
    public void CreateFor_wraps_inner_backend_with_CapturingBackend_on_success()
    {
        var fake = new FakeBackend();
        var inner = new FakeBackendFactory(fake);
        var factory = new CapturingBackendFactory(inner, NullTrafficWriter.Instance);

        var result = factory.CreateFor(Dev("psu1"));

        result.ShouldBeOk().ShouldBeOfType<CapturingBackend>();
    }

    [Fact]
    public void CreateFor_passes_inner_failure_through_unchanged()
    {
        var inner = new RejectingFactory();
        var factory = new CapturingBackendFactory(inner, NullTrafficWriter.Instance);

        var result = factory.CreateFor(Dev("psu1"));

        result.ShouldBeError().ShouldBeOfType<UnsupportedTransport>();
    }

    private sealed class RejectingFactory : IBackendFactory
    {
        public Result<IIviBackend, BackendError> CreateFor(Device device) =>
            Result.Failure<IIviBackend, BackendError>(new UnsupportedTransport(device.Name));
    }
}
