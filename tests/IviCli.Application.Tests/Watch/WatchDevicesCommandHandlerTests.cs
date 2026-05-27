using System.Collections.Immutable;
using IviCli.Application.Devices;
using IviCli.Application.Watch;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.TestKit;

namespace IviCli.Application.Tests.Watch;

public sealed class WatchDevicesCommandHandlerTests
{
    private static Device Dev(string name) =>
        new(
            DeviceName.From(name).ShouldBeOk(),
            VisaResource.Parse("TCPIP0::host::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    private static FakeConfigStore Store(params Device[] seed)
    {
        var doc = ConfigDocument.Empty;
        foreach (var d in seed)
        {
            doc = doc.AddDevice(d).ShouldBeOk();
        }
        return new FakeConfigStore(doc);
    }

    [Fact]
    public async Task HandleAsync_returns_WatchNoDevices_when_config_has_no_devices()
    {
        var handler = new WatchDevicesCommandHandler(Store(), new RecordingProbe());
        var sink = new CollectingSink();

        var result = await handler.HandleAsync(
            new WatchDevicesCommand(null, TimeSpan.FromMilliseconds(50), MaxIterations: 1),
            sink,
            CancellationToken.None
        );

        result.ShouldBeError().ShouldBeOfType<WatchNoDevices>();
        sink.Ticks.ShouldBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_returns_WatchUnknownDevice_for_missing_alias()
    {
        var handler = new WatchDevicesCommandHandler(Store(Dev("psu1")), new RecordingProbe());

        var result = await handler.HandleAsync(
            new WatchDevicesCommand(
                ImmutableArray.Create("psu1", "psu2"),
                TimeSpan.FromMilliseconds(50),
                MaxIterations: 1
            ),
            new CollectingSink(),
            CancellationToken.None
        );

        var err = result.ShouldBeError().ShouldBeOfType<WatchUnknownDevice>();
        err.Name.Value.ShouldBe("psu2");
    }

    [Fact]
    public async Task HandleAsync_invalid_interval_is_rejected()
    {
        var handler = new WatchDevicesCommandHandler(Store(Dev("psu1")), new RecordingProbe());

        var result = await handler.HandleAsync(
            new WatchDevicesCommand(null, TimeSpan.Zero, MaxIterations: 1),
            new CollectingSink(),
            CancellationToken.None
        );

        result.ShouldBeError().ShouldBeOfType<WatchInvalidInterval>();
    }

    [Fact]
    public async Task HandleAsync_emits_one_tick_per_iteration_up_to_MaxIterations()
    {
        var probe = new RecordingProbe();
        var handler = new WatchDevicesCommandHandler(Store(Dev("psu1"), Dev("dmm1")), probe);
        var sink = new CollectingSink();

        var result = await handler.HandleAsync(
            new WatchDevicesCommand(null, TimeSpan.FromMilliseconds(1), MaxIterations: 3),
            sink,
            CancellationToken.None
        );

        result.ShouldBeOk();
        sink.Ticks.Count.ShouldBe(3);
        sink.Ticks[0].Sequence.ShouldBe(0);
        sink.Ticks[2].Sequence.ShouldBe(2);
        sink.Ticks.ShouldAllBe(t => t.Snapshots.Length == 2);
        probe.Calls.Count.ShouldBe(6); // 2 devices × 3 iterations
    }

    [Fact]
    public async Task HandleAsync_explicit_names_only_probe_subset()
    {
        var probe = new RecordingProbe();
        var handler = new WatchDevicesCommandHandler(
            Store(Dev("psu1"), Dev("dmm1"), Dev("scope1")),
            probe
        );
        var sink = new CollectingSink();

        await handler.HandleAsync(
            new WatchDevicesCommand(
                ImmutableArray.Create("dmm1", "scope1"),
                TimeSpan.FromMilliseconds(1),
                MaxIterations: 1
            ),
            sink,
            CancellationToken.None
        );

        sink.Ticks.Single().Snapshots.Select(s => s.Device.Name.Value).ShouldBe(["dmm1", "scope1"]);
    }

    [Fact]
    public async Task HandleAsync_returns_success_on_cancellation_without_throwing()
    {
        var probe = new RecordingProbe();
        var handler = new WatchDevicesCommandHandler(Store(Dev("psu1")), probe);
        var sink = new CollectingSink();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var result = await handler.HandleAsync(
            new WatchDevicesCommand(null, TimeSpan.FromSeconds(5), MaxIterations: null),
            sink,
            cts.Token
        );

        result.ShouldBeOk();
    }

    private sealed class CollectingSink : IWatchDevicesSink
    {
        public List<WatchTick> Ticks { get; } = new();

        public Task EmitAsync(WatchTick tick, CancellationToken ct)
        {
            Ticks.Add(tick);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingProbe : IDeviceStatusProbe
    {
        public List<DeviceName> Calls { get; } = new();

        public Task<DeviceStatus> ProbeAsync(Device device, CancellationToken ct)
        {
            Calls.Add(device.Name);
            return Task.FromResult(
                new DeviceStatus(
                    device,
                    IsOnline: true,
                    ResponseTime: TimeSpan.FromMilliseconds(1),
                    IdnResponse: "FAKE,DEV,000,0.0",
                    FailureMessage: null
                )
            );
        }
    }
}
