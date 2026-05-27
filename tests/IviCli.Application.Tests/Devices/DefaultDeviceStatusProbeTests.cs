using IviCli.Application.Backends;
using IviCli.Application.Devices;
using IviCli.Backends.Fake;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;
using IviCli.TestKit;

namespace IviCli.Application.Tests.Devices;

/// <summary>
/// Behavioural tests for <see cref="DefaultDeviceStatusProbe"/>. The probe
/// owns the Stopwatch / OpenAsync / *IDN? / CloseAsync sequence that the
/// <c>visa status</c> and <c>visa watch</c> handlers both rely on.
/// </summary>
public sealed class DefaultDeviceStatusProbeTests
{
    private static Device Dev(string name) =>
        new(
            DeviceName.From(name).ShouldBeOk(),
            VisaResource.Parse("TCPIP0::host::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    [Fact]
    public async Task ProbeAsync_returns_online_when_idn_query_succeeds()
    {
        var backend = new FakeBackend();
        var device = Dev("psu1");
        backend.ConfigureDevice(device.Name, "ACME,PSU,001,1.0");
        var probe = new DefaultDeviceStatusProbe(new FakeBackendFactory(backend));

        var status = await probe.ProbeAsync(device, CancellationToken.None);

        status.IsOnline.ShouldBeTrue();
        status.IdnResponse.ShouldBe("ACME,PSU,001,1.0");
        status.FailureMessage.ShouldBeNull();
    }

    [Fact]
    public async Task ProbeAsync_returns_offline_with_message_when_backend_rejects_resolution()
    {
        var probe = new DefaultDeviceStatusProbe(new RejectingFactory());
        var status = await probe.ProbeAsync(Dev("psu1"), CancellationToken.None);

        status.IsOnline.ShouldBeFalse();
        status.FailureMessage.ShouldNotBeNullOrEmpty();
        status.IdnResponse.ShouldBeNull();
    }

    private sealed class RejectingFactory : IBackendFactory
    {
        public Result<IIviBackend, BackendError> CreateFor(Device device) =>
            Result.Failure<IIviBackend, BackendError>(new UnsupportedTransport(device.Name));
    }
}
