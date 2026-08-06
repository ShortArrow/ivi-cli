using System.Reflection;
using IviCli.Application.Backends;
using IviCli.Backends.Local;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;
using Xunit;

namespace IviCli.Backends.Local.Tests;

/// <summary>
/// Runtime smoke tests for <see cref="VisaSessionFactory"/>
/// against an installed IVI Shared Components runtime
/// (NI-VISA / Keysight VISA / compatible) — see ADR 0037.
///
/// These tests exercise the VISA.NET runtime plumbing
/// but never assume a physical instrument is attached. Failures surface
/// as <see cref="BackendError"/> values, not exceptions. The
/// <c>[Requires("ni-visa")]</c> gate cache-loads <c>Ivi.Visa</c> once per
/// test process; on machines without the runtime, all three tests skip
/// with a precise reason string.
/// </summary>
public sealed class LocalBackendVisaInteropTests
{
    private static Device Dev(string resource) =>
        new(
            DeviceName.From("dut").ShouldBeOk(),
            VisaResource.Parse(resource).ShouldBeOk(),
            Timeout.FromMilliseconds(500).ShouldBeOk()
        );

    [Requires("ni-visa")]
    [Trait("Category", "Integration")]
    public void Reflection_bindings_resolve_GlobalResourceManager_without_throwing()
    {
        // The probe already established Ivi.Visa loads; confirm the
        // factory's lazy reflection path is reachable without raising.
        var assembly = Assembly.Load("Ivi.Visa");
        assembly
            .GetType("Ivi.Visa.GlobalResourceManager")
            .ShouldNotBeNull(
                "the IVI shared components assembly must expose GlobalResourceManager"
            );
        assembly
            .GetType("Ivi.Visa.IMessageBasedSession")
            .ShouldNotBeNull("the IVI shared components assembly must expose IMessageBasedSession");

        var factory = new VisaSessionFactory();
        // First-use trigger: a deliberately-invalid resource should at
        // worst return BackendError — never throw — confirming the
        // bindings record initialised cleanly.
        var backend = new LocalBackend(factory);
        var result = backend
            .OpenAsync(Dev("TCPIP0::0.0.0.0::inst0::INSTR"), default)
            .GetAwaiter()
            .GetResult();
        result.ShouldBeOfType<Result<Unit, BackendError>.Error>();
    }

    [Requires("ni-visa")]
    [Trait("Category", "Integration")]
    public async Task OpenAsync_against_obviously_invalid_resource_returns_BackendError()
    {
        var backend = new LocalBackend(new VisaSessionFactory());

        // 0.0.0.0 is not a routeable destination; VISA must reject the
        // session open rather than block indefinitely. The 500 ms device
        // timeout caps how long we wait.
        var result = await backend.OpenAsync(Dev("TCPIP0::0.0.0.0::inst0::INSTR"), default);

        result.ShouldBeOfType<Result<Unit, BackendError>.Error>();
    }

    [Requires("ni-visa")]
    [Trait("Category", "Integration")]
    public async Task OpenAsync_against_loopback_socket_resource_invokes_reflection_path()
    {
        // 127.0.0.1:nothing-listening exercises the reflection path
        // through to the VISA runtime's TCP code; the failure must
        // surface as a clean BackendError (no thrown exception).
        var backend = new LocalBackend(new VisaSessionFactory());
        var result = await backend.OpenAsync(Dev("TCPIP0::127.0.0.1::inst0::INSTR"), default);

        result.ShouldBeOfType<Result<Unit, BackendError>.Error>();
    }
}
