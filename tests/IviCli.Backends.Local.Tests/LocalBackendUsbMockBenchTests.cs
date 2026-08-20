using Ivi.Visa;
using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;
using Xunit;

namespace IviCli.Backends.Local.Tests;

/// <summary>
/// Acceptance evidence for issue #18 (ADR 0041): the Local NI-VISA
/// backend's <c>ServiceRequestStream</c> and <c>TriggerAsync</c> against
/// the virtual USB mock instrument (ADR 0049) attached through the
/// host's USB/IP client. Runs only when a VISA runtime is installed and
/// the mock is attached with the scenario in
/// <c>Assets/usb-srq-bench.scenario.toml</c> (setup steps are in that
/// file's header); skips everywhere else, including CI.
/// </summary>
public sealed class LocalBackendUsbMockBenchTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static Device AttachedMock()
    {
        var resource = GlobalResourceManager.Find("USB?*::0x1209::0x0001::?*::INSTR").First();
        return new Device(
            DeviceName.From("dut").ShouldBeOk(),
            VisaResource.Parse(resource).ShouldBeOk(),
            Timeout.FromMilliseconds(5000).ShouldBeOk()
        );
    }

    private static async Task WriteAsync(LocalBackend backend, Device device, string line) =>
        (
            await backend.WriteAsync(device, ScpiCommand.From(line).ShouldBeOk(), default)
        ).ShouldBeOk();

    private static async Task AssertBenchScenarioIsActive(LocalBackend backend, Device device)
    {
        var idn = await backend.QueryAsync(device, ScpiQuery.From("*IDN?").ShouldBeOk(), default);
        idn.ShouldBeOk()
            .ShouldBe(
                "IVICLI,SRQ-BENCH,0,1.0",
                "the attached mock must serve Assets/usb-srq-bench.scenario.toml"
            );
    }

    [Requires("ni-visa", "usb-mock")]
    [Trait("Category", "Integration")]
    public async Task The_488_2_sequence_delivers_an_srq_through_ServiceRequestStream()
    {
        // Given the mock attached over USB/IP, with the stream armed before
        // the completing operation is sent (the enumerator body runs
        // synchronously up to its first await, so the subscription is
        // enabled once MoveNextAsync has been called)
        var backend = new LocalBackend(new VisaSessionFactory());
        var device = AttachedMock();
        (await backend.OpenAsync(device, default)).ShouldBeOk();
        try
        {
            await AssertBenchScenarioIsActive(backend, device);
            using var cts = new CancellationTokenSource(Patience);
            await using var stream = backend
                .ServiceRequestStream(device, cts.Token)
                .GetAsyncEnumerator(cts.Token);
            var firstSrq = stream.MoveNextAsync();

            // When the IEEE 488.2 arming sequence and *OPC are written
            foreach (var line in new[] { "*CLS", "*ESE 1", "*SRE 32", "*OPC" })
            {
                await WriteAsync(backend, device, line);
            }

            // Then the rule-raised service request surfaces on the stream
            (await firstSrq).ShouldBeTrue("no service request arrived within patience");
            stream.Current.Device.ShouldBe(device.Name);
            stream.Current.StatusByte.ShouldBe((byte)0x60);
        }
        finally
        {
            await backend.CloseAsync(device, default);
        }
    }

    [Requires("ni-visa", "usb-mock")]
    [Trait("Category", "Integration")]
    public async Task TriggerAsync_reaches_the_instrument_as_TRG()
    {
        // Given the mock attached over USB/IP and the stream armed
        var backend = new LocalBackend(new VisaSessionFactory());
        var device = AttachedMock();
        (await backend.OpenAsync(device, default)).ShouldBeOk();
        try
        {
            await AssertBenchScenarioIsActive(backend, device);
            using var cts = new CancellationTokenSource(Patience);
            await using var stream = backend
                .ServiceRequestStream(device, cts.Token)
                .GetAsyncEnumerator(cts.Token);
            var firstSrq = stream.MoveNextAsync();

            // When the backend asserts a trigger
            (await backend.TriggerAsync(device, default)).ShouldBeOk();

            // Then the *TRG rule's distinct status byte proves the trigger
            // reached the device (0x41, not the *OPC rule's 0x60)
            (await firstSrq).ShouldBeTrue("no service request arrived within patience");
            stream.Current.Device.ShouldBe(device.Name);
            stream.Current.StatusByte.ShouldBe((byte)0x41);
        }
        finally
        {
            await backend.CloseAsync(device, default);
        }
    }
}
