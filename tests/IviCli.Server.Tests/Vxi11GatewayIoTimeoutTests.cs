using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using IviCli.Application.Backends;
using IviCli.Backends.Vxi11;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Protocols;
using IviCli.Domain.Scpi;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.Server.Vxi11;
using IviCli.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using static IviCli.Domain.Protocols.Vxi11Constants;
using static IviCli.Server.Tests.Vxi11Rpc;
using RecordFraming = IviCli.Server.Vxi11.Vxi11RecordFraming;

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
    [Spec(Standard.Vxi11, "RULE B.6.19")]
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
    [Spec(Standard.Vxi11, "B.4.3")]
    [Spec(Standard.Adr, "0053 §5")]
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
    [Spec(Standard.Vxi11, "RULE B.6.19")]
    public async Task A_write_past_the_clients_io_timeout_ends_with_a_timeout()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var bench = Bench.Start(
            new Instrument(answerAfter: System.Threading.Timeout.InfiniteTimeSpan),
            gatewayDeviceTimeout: Long,
            ioTimeout: Short,
            cts.Token
        );

        var result = await bench.Client.WriteAsync(
            bench.Device,
            ScpiCommand.From("*RST").ShouldBeOk(),
            cts.Token
        );

        result
            .ShouldBeOfType<Result<Unit, BackendError>.Error>()
            .Err.ShouldBeOfType<TransportTimeout>();
    }

    /// <summary>
    /// An <c>io_timeout</c> of 0 leaves the routed device's timeout in
    /// place instead of failing every call at once.
    /// </summary>
    [Fact]
    [Spec(Standard.Adr, "0053 §5")]
    public async Task A_zero_io_timeout_falls_back_to_the_device_timeout()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var bench = Bench.Start(
            new Instrument(answerAfter: Short),
            gatewayDeviceTimeout: TimeSpan.FromSeconds(5),
            ioTimeout: TimeSpan.Zero,
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
    [Spec(Standard.Vxi11, "RULE B.6.44")]
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
    /// A long <c>lock_timeout</c> with the waitlock flag set does not extend
    /// a call past its <c>io_timeout</c>: the gateway implements no
    /// <c>device_lock</c>, so no call waits for a lock.
    /// </summary>
    [Fact]
    [Spec(Standard.Adr, "0053 §5")]
    public async Task A_long_lock_timeout_does_not_extend_a_call_past_its_io_timeout()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var bench = Bench.Start(
            new Instrument(answerAfter: System.Threading.Timeout.InfiniteTimeSpan),
            gatewayDeviceTimeout: Long,
            ioTimeout: Short,
            cts.Token
        );

        var watch = Stopwatch.StartNew();
        var error = await RawWriteAsync(
            bench.Port,
            ioTimeoutMs: 300,
            lockTimeoutMs: 60_000,
            cts.Token
        );
        watch.Stop();

        error.ShouldBe(Vxi11IoTimeout);
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// A short <c>lock_timeout</c> with the waitlock flag set does not cut a
    /// call short of its <c>io_timeout</c>.
    /// </summary>
    [Fact]
    [Spec(Standard.Adr, "0053 §5")]
    public async Task A_short_lock_timeout_does_not_cut_a_call_short_of_its_io_timeout()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var bench = Bench.Start(
            new Instrument(answerAfter: Short),
            gatewayDeviceTimeout: Long,
            ioTimeout: Long,
            cts.Token
        );

        var error = await RawWriteAsync(
            bench.Port,
            ioTimeoutMs: 5_000,
            lockTimeoutMs: 1,
            cts.Token
        );

        error.ShouldBe(Vxi11NoError);
    }

    /// <summary>
    /// Links to the gateway on <paramref name="port"/> and sends one
    /// <c>device_write</c> with the waitlock flag and the given timeouts.
    /// </summary>
    /// <returns>The error code of the <c>device_write</c> reply.</returns>
    private static async Task<int> RawWriteAsync(
        int port,
        uint ioTimeoutMs,
        uint lockTimeoutMs,
        CancellationToken ct
    )
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port, ct);
        using var stream = tcp.GetStream();
        var create = BuildRpcCall(
            xid: 1,
            program: CoreProgram,
            version: CoreVersion,
            procedure: ProcCreateLink,
            body: writer =>
            {
                writer.WriteInt32(2); // clientId
                writer.WriteUInt32(0); // lockDevice
                writer.WriteUInt32(0); // lock_timeout
                writer.WriteString("inst0");
            }
        );
        await RecordFraming.WriteRecordAsync(stream, create, ct);
        var created = SkipReplyHeader(await RecordFraming.ReadRecordAsync(stream, ct));
        created.ReadInt32().ShouldBe(Vxi11NoError);
        var lid = created.ReadInt32();

        var write = BuildRpcCall(
            xid: 2,
            program: CoreProgram,
            version: CoreVersion,
            procedure: ProcDeviceWrite,
            body: writer =>
            {
                writer.WriteInt32(lid);
                writer.WriteUInt32(ioTimeoutMs);
                writer.WriteUInt32(lockTimeoutMs);
                writer.WriteInt32(WaitLockFlag | WriteEndFlag);
                writer.WriteOpaque("*RST\n"u8.ToArray());
            }
        );
        await RecordFraming.WriteRecordAsync(stream, write, ct);
        return SkipReplyHeader(await RecordFraming.ReadRecordAsync(stream, ct)).ReadInt32();
    }

    /// <summary>
    /// A gateway routed to <paramref name="instrument"/> and a client linked
    /// to it, both on loopback.
    /// </summary>
    private sealed class Bench : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop;
        private readonly Task _run;

        private Bench(
            Vxi11Backend client,
            Device device,
            int port,
            CancellationTokenSource stop,
            Task run
        )
        {
            Client = client;
            Port = port;
            Device = device;
            _stop = stop;
            _run = run;
        }

        public Vxi11Backend Client { get; }
        public Device Device { get; }

        /// <summary>The gateway's loopback port, for a client of the test's own.</summary>
        public int Port { get; }

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
            return new Bench(client, device, port, stop, run);
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
                IviCli.Domain.Servers.Port.From(LoopbackGateway.FreePort()).ShouldBeOk()
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
    /// An instrument that accepts a command, answers a query and completes
    /// a trigger only after <c>answerAfter</c>.
    /// </summary>
    private sealed class Instrument(TimeSpan answerAfter) : IIviBackend
    {
        public const string Answer = "ACME,SLOW,0,1.0";

        public Task<Result<Unit, BackendError>> OpenAsync(Device device, CancellationToken ct) =>
            Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

        public Task<Result<Unit, BackendError>> CloseAsync(Device device, CancellationToken ct) =>
            Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

        public async Task<Result<Unit, BackendError>> WriteAsync(
            Device device,
            ScpiCommand command,
            CancellationToken ct
        )
        {
            await Task.Delay(answerAfter, ct);
            return Result.Success<Unit, BackendError>(Unit.Value);
        }

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
