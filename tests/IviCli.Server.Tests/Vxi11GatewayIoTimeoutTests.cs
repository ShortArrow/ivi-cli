using System.Runtime.CompilerServices;
using IviCli.Application.Backends;
using IviCli.Backends.Vxi11;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.Server.Vxi11;
using IviCli.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace IviCli.Server.Tests;

/// <summary>
/// The VXI-11 gateway gives each call the <c>io_timeout</c> the client sent
/// instead of the routed device's own timeout: the server allows at least
/// <c>io_timeout</c> for the operation and ends a call that runs past it
/// with error 15 (VXI-11 Rev 1.0, B.4.3, RULE B.6.19, B.6.44). The gateway's
/// backends are composed inside <see cref="DeviceTimeoutBackendFactory"/>,
/// as the CLI composes them, and the client is a bare
/// <see cref="Vxi11Backend"/>, which sends its device timeout as
/// <c>io_timeout</c> and has no deadline of its own.
/// </summary>
public sealed class Vxi11GatewayIoTimeoutTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task A_query_past_the_clients_io_timeout_ends_with_a_timeout()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var bench = Bench.Start(
            new Instrument(answerAfter: System.Threading.Timeout.InfiniteTimeSpan),
            gatewayDeviceTimeout: Long,
            ioTimeout: Short,
            cts.Token
        );

        var result = await bench.Client.QueryAsync(
            bench.Device,
            ScpiQuery.From("*IDN?").ShouldBeOk(),
            cts.Token
        );

        result
            .ShouldBeOfType<Result<string, BackendError>.Error>()
            .Err.ShouldBeOfType<TransportTimeout>();
    }

    [Fact]
    public async Task A_query_within_the_clients_io_timeout_is_answered_past_the_device_timeout()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var bench = Bench.Start(
            new Instrument(answerAfter: TimeSpan.FromSeconds(1)),
            gatewayDeviceTimeout: Short,
            ioTimeout: TimeSpan.FromSeconds(5),
            cts.Token
        );

        var result = await bench.Client.QueryAsync(
            bench.Device,
            ScpiQuery.From("*IDN?").ShouldBeOk(),
            cts.Token
        );

        result.ShouldBeOk().ShouldBe(Instrument.Answer);
    }

    [Fact]
    public async Task A_trigger_past_the_clients_io_timeout_ends_with_a_timeout()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var bench = Bench.Start(
            new Instrument(answerAfter: System.Threading.Timeout.InfiniteTimeSpan),
            gatewayDeviceTimeout: Long,
            ioTimeout: Short,
            cts.Token
        );

        var result = await bench.Client.TriggerAsync(bench.Device, cts.Token);

        result
            .ShouldBeOfType<Result<Unit, BackendError>.Error>()
            .Err.ShouldBeOfType<TransportTimeout>();
    }

    /// <summary>
    /// A gateway routed to <paramref name="instrument"/> and a client linked
    /// to it, both on loopback.
    /// </summary>
    private sealed class Bench : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop;
        private readonly Task _run;

        private Bench(Vxi11Backend client, Device device, CancellationTokenSource stop, Task run)
        {
            Client = client;
            Device = device;
            _stop = stop;
            _run = run;
        }

        public Vxi11Backend Client { get; }
        public Device Device { get; }

        public static Bench Start(
            IIviBackend instrument,
            TimeSpan gatewayDeviceTimeout,
            TimeSpan ioTimeout,
            CancellationToken ct
        )
        {
            var gateway = new Vxi11GatewayServer(
                new DeviceTimeoutBackendFactory(
                    new FakeBackendFactory(instrument),
                    TimeProvider.System
                ),
                NullLogger<Vxi11GatewayServer>.Instance
            )
            {
                PortmapUdpPort = LoopbackGateway.FreePort(),
            };
            var (server, config) = Topology(gatewayDeviceTimeout);
            var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var (port, run) = LoopbackGateway.Start(
                server,
                s => gateway.RunAsync(s, config, stop.Token)
            );
            var client = new Vxi11Backend(port);
            var device = new Device(
                DeviceName.From("remote").ShouldBeOk(),
                VisaResource.Parse($"TCPIP0::127.0.0.1::inst0,{port}::INSTR").ShouldBeOk(),
                Timeout.From(ioTimeout).ShouldBeOk()
            );
            client.OpenAsync(device, ct).GetAwaiter().GetResult().ShouldBeOk();
            return new Bench(client, device, stop, run);
        }

        public async ValueTask DisposeAsync()
        {
            await Client.CloseAsync(Device, CancellationToken.None);
            await _stop.CancelAsync();
            try
            {
                await _run;
            }
            catch (OperationCanceledException) { }
            _stop.Dispose();
        }

        private static (IviCli.Domain.Servers.Server Server, ConfigDocument Config) Topology(
            TimeSpan deviceTimeout
        )
        {
            var deviceName = DeviceName.From("dut").ShouldBeOk();
            var device = new Device(
                deviceName,
                VisaResource.Parse("TCPIP0::127.0.0.1::INSTR").ShouldBeOk(),
                Timeout.From(deviceTimeout).ShouldBeOk()
            );
            var server = new IviCli.Domain.Servers.Server(
                ServerName.From("srv").ShouldBeOk(),
                ServerType.Vxi11,
                IpAddress.From("127.0.0.1").ShouldBeOk(),
                Port.From(LoopbackGateway.FreePort()).ShouldBeOk()
            );
            var config = ConfigDocument
                .Empty.AddDevice(device)
                .ShouldBeOk()
                .AddServer(server)
                .ShouldBeOk()
                .AddRoute(
                    new Route(server.Name, PublicEndpoint.From("inst0").ShouldBeOk(), deviceName)
                )
                .ShouldBeOk();
            return (server, config);
        }
    }

    /// <summary>
    /// An instrument that accepts every command at once and answers a query
    /// or completes a trigger only after <c>answerAfter</c>.
    /// </summary>
    private sealed class Instrument(TimeSpan answerAfter) : IIviBackend
    {
        public const string Answer = "ACME,SLOW,0,1.0";

        public Task<Result<Unit, BackendError>> OpenAsync(Device device, CancellationToken ct) =>
            Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

        public Task<Result<Unit, BackendError>> CloseAsync(Device device, CancellationToken ct) =>
            Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

        public Task<Result<Unit, BackendError>> WriteAsync(
            Device device,
            ScpiCommand command,
            CancellationToken ct
        ) => Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

        public async Task<Result<string, BackendError>> QueryAsync(
            Device device,
            ScpiQuery query,
            CancellationToken ct
        )
        {
            await Task.Delay(answerAfter, ct);
            return Result.Success<string, BackendError>(Answer);
        }

        public Task<Result<string, BackendError>> ReadAsync(Device device, CancellationToken ct) =>
            Task.FromResult(Result.Success<string, BackendError>(Answer));

        public async Task<Result<Unit, BackendError>> TriggerAsync(
            Device device,
            CancellationToken ct
        )
        {
            await Task.Delay(answerAfter, ct);
            return Result.Success<Unit, BackendError>(Unit.Value);
        }

        public async IAsyncEnumerable<ServiceRequest> ServiceRequestStream(
            Device device,
            [EnumeratorCancellation] CancellationToken ct
        )
        {
            await Task.Delay(System.Threading.Timeout.Infinite, ct);
            yield break;
        }
    }
}
