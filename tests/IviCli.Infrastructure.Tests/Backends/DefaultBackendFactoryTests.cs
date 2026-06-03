using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;
using IviCli.Infrastructure.Backends;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Infrastructure.Tests.Backends;

/// <summary>
/// Dispatch tests for <see cref="DefaultBackendFactory"/>'s resource-string
/// resolver. The fakes count which backend was selected so we can assert
/// the LanDevice discriminator wires up the right transport.
/// </summary>
public sealed class DefaultBackendFactoryTests
{
    [Fact]
    public void Inst0_resource_selects_vxi11_backend()
    {
        var fallback = new MarkerBackend("fallback");
        var local = new MarkerBackend("local");
        var hislip = new MarkerBackend("hislip");
        var vxi11 = new MarkerBackend("vxi11");
        var factory = new DefaultBackendFactory(
            fallback,
            localBackend: local,
            hislipBackend: hislip,
            vxi11Backend: vxi11
        );

        var device = BuildDevice("TCPIP0::127.0.0.1::inst0::INSTR");
        var picked = ((MarkerBackend)factory.CreateFor(device).ShouldBeOk()).Tag;

        picked.ShouldBe("vxi11");
    }

    [Fact]
    public void Active_scenario_on_fallback_outranks_resource_shape_dispatch()
    {
        // Issue #25: when the fallback backend reports an active mock
        // scenario, every dispatch must collapse to the fallback so
        // the gateway can answer from scenes instead of trying — and
        // timing out on — a real transport connection. The
        // TCPIP0::host::inst0 resource normally routes to the VXI-11
        // backend; here it must NOT.
        var fallback = new ScenarioAwareMarkerBackend("fallback", hasActiveScenario: true);
        var vxi11 = new MarkerBackend("vxi11");
        var hislip = new MarkerBackend("hislip");
        var factory = new DefaultBackendFactory(
            fallback,
            hislipBackend: hislip,
            vxi11Backend: vxi11
        );

        var vxi11Device = BuildDevice("TCPIP0::127.0.0.1::inst0::INSTR");
        var hislipDevice = BuildDevice("TCPIP0::127.0.0.1::hislip0::INSTR");
        var socketDevice = BuildDevice("TCPIP0::127.0.0.1::5025::SOCKET");

        ((ScenarioAwareMarkerBackend)factory.CreateFor(vxi11Device).ShouldBeOk()).Tag.ShouldBe(
            "fallback"
        );
        ((ScenarioAwareMarkerBackend)factory.CreateFor(hislipDevice).ShouldBeOk()).Tag.ShouldBe(
            "fallback"
        );
        ((ScenarioAwareMarkerBackend)factory.CreateFor(socketDevice).ShouldBeOk()).Tag.ShouldBe(
            "fallback"
        );
    }

    [Fact]
    public void Inactive_scenario_on_fallback_does_not_outrank_resource_shape_dispatch()
    {
        // The flip side of the scenario-aware shortcut: when the
        // fallback reports no active scenario, dispatch follows the
        // VisaResource shape exactly as before, so real transport
        // backends still receive their normal traffic.
        var fallback = new ScenarioAwareMarkerBackend("fallback", hasActiveScenario: false);
        var vxi11 = new MarkerBackend("vxi11");
        var factory = new DefaultBackendFactory(fallback, vxi11Backend: vxi11);

        var device = BuildDevice("TCPIP0::127.0.0.1::inst0::INSTR");
        var picked = ((MarkerBackend)factory.CreateFor(device).ShouldBeOk()).Tag;

        picked.ShouldBe("vxi11");
    }

    [Fact]
    public void Hislip0_resource_still_selects_hislip_even_when_inst_prefix_resolver_present()
    {
        var fallback = new MarkerBackend("fallback");
        var hislip = new MarkerBackend("hislip");
        var vxi11 = new MarkerBackend("vxi11");
        var factory = new DefaultBackendFactory(
            fallback,
            hislipBackend: hislip,
            vxi11Backend: vxi11
        );

        var device = BuildDevice("TCPIP0::127.0.0.1::hislip0::INSTR");
        var picked = ((MarkerBackend)factory.CreateFor(device).ShouldBeOk()).Tag;

        picked.ShouldBe("hislip");
    }

    private static Device BuildDevice(string resource) =>
        new(
            DeviceName.From("d").ShouldBeOk(),
            VisaResource.Parse(resource).ShouldBeOk(),
            Timeout.FromMilliseconds(1000).ShouldBeOk()
        );

    private sealed class MarkerBackend : IIviBackend
    {
        public MarkerBackend(string tag)
        {
            Tag = tag;
        }

        public string Tag { get; }

        public Task<Result<Unit, BackendError>> OpenAsync(Device device, CancellationToken ct) =>
            Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

        public Task<Result<Unit, BackendError>> CloseAsync(Device device, CancellationToken ct) =>
            Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

        public Task<Result<Unit, BackendError>> WriteAsync(
            Device device,
            ScpiCommand command,
            CancellationToken ct
        ) => Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

        public Task<Result<string, BackendError>> QueryAsync(
            Device device,
            ScpiQuery query,
            CancellationToken ct
        ) => Task.FromResult(Result.Success<string, BackendError>(Tag));

        public Task<Result<string, BackendError>> ReadAsync(Device device, CancellationToken ct) =>
            Task.FromResult(Result.Success<string, BackendError>(Tag));

        public Task<Result<Unit, BackendError>> TriggerAsync(Device device, CancellationToken ct) =>
            Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

#pragma warning disable CS1998
        public async IAsyncEnumerable<ServiceRequest> ServiceRequestStream(
            Device device,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct
        )
        {
            yield break;
        }
#pragma warning restore CS1998
    }

    private sealed class ScenarioAwareMarkerBackend : IScenarioAwareBackend
    {
        public ScenarioAwareMarkerBackend(string tag, bool hasActiveScenario)
        {
            Tag = tag;
            HasActiveScenario = hasActiveScenario;
        }

        public string Tag { get; }
        public bool HasActiveScenario { get; }

        public Task<Result<Unit, BackendError>> OpenAsync(Device device, CancellationToken ct) =>
            Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

        public Task<Result<Unit, BackendError>> CloseAsync(Device device, CancellationToken ct) =>
            Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

        public Task<Result<Unit, BackendError>> WriteAsync(
            Device device,
            ScpiCommand command,
            CancellationToken ct
        ) => Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

        public Task<Result<string, BackendError>> QueryAsync(
            Device device,
            ScpiQuery query,
            CancellationToken ct
        ) => Task.FromResult(Result.Success<string, BackendError>(Tag));

        public Task<Result<string, BackendError>> ReadAsync(Device device, CancellationToken ct) =>
            Task.FromResult(Result.Success<string, BackendError>(Tag));

        public Task<Result<Unit, BackendError>> TriggerAsync(Device device, CancellationToken ct) =>
            Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

#pragma warning disable CS1998
        public async IAsyncEnumerable<ServiceRequest> ServiceRequestStream(
            Device device,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct
        )
        {
            yield break;
        }
#pragma warning restore CS1998
    }
}
