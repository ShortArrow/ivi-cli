using System.Diagnostics;
using System.Runtime.CompilerServices;
using IviCli.Application.Backends;
using IviCli.Application.Servers;
using IviCli.Backends.HiSlip;
using IviCli.Backends.Socket;
using IviCli.Backends.Vxi11;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.Server.HiSlip;
using IviCli.Server.Socket;
using IviCli.Server.Vxi11;
using IviCli.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace IviCli.Server.Tests;

/// <summary>
/// A device's <c>timeout_ms</c> bounds a query on every network backend: an
/// instrument that never answers makes the query fail with
/// <see cref="TransportTimeout"/> once that time has passed, instead of
/// leaving the caller waiting. Each case puts an instrument that never
/// answers behind ivi-cli's own gateway and queries it with the client
/// backend for that transport, composed as the CLI composes it, inside
/// <see cref="DeviceTimeoutBackendFactory"/>.
/// </summary>
public sealed class ClientTimeoutTests
{
    private static readonly TimeSpan DeviceTimeout = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(3);

    public static TheoryData<ServerType> Transports =>
        new() { ServerType.HiSlip, ServerType.Vxi11, ServerType.Socket };

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task A_query_to_a_silent_instrument_times_out_after_the_device_timeout(
        ServerType transport
    )
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var gateway = Gateway(transport);
        var (gatewayServer, config) = Topology(transport);
        var (port, run) = LoopbackGateway.Start(
            gatewayServer,
            s => gateway.RunAsync(s, config, cts.Token)
        );
        var (client, device) = Client(transport, port);

        (await client.OpenAsync(device, cts.Token)).ShouldBeOk();
        var watch = Stopwatch.StartNew();
        var result = await client.QueryAsync(
            device,
            ScpiQuery.From("*IDN?").ShouldBeOk(),
            cts.Token
        );
        watch.Stop();

        watch.Elapsed.ShouldBeLessThan(Patience);
        result
            .ShouldBeOfType<Result<string, BackendError>.Error>()
            .Err.ShouldBeOfType<TransportTimeout>();

        await client.CloseAsync(device, CancellationToken.None);
        await cts.CancelAsync();
        try
        {
            await run;
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// VXI-11 carries a timeout as error 15 (VXI-11 Rev 1.0, B.6.3): an
    /// instrument behind ivi-cli's gateway that times out reaches the
    /// client as <see cref="TransportTimeout"/>, not as a lost connection.
    /// </summary>
    [Fact]
    public async Task A_timeout_behind_the_VXI11_gateway_reaches_the_client_as_a_timeout()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var gateway = new Vxi11GatewayServer(
            new FakeBackendFactory(new TimingOutInstrument()),
            NullLogger<Vxi11GatewayServer>.Instance
        )
        {
            PortmapUdpPort = LoopbackGateway.FreePort(),
        };
        var (gatewayServer, config) = Topology(ServerType.Vxi11);
        var (port, run) = LoopbackGateway.Start(
            gatewayServer,
            s => gateway.RunAsync(s, config, cts.Token)
        );
        var (client, device) = Client(ServerType.Vxi11, port);

        (await client.OpenAsync(device, cts.Token)).ShouldBeOk();
        var result = await client.QueryAsync(
            device,
            ScpiQuery.From("*IDN?").ShouldBeOk(),
            cts.Token
        );

        result
            .ShouldBeOfType<Result<string, BackendError>.Error>()
            .Err.ShouldBeOfType<TransportTimeout>();

        await client.CloseAsync(device, CancellationToken.None);
        await cts.CancelAsync();
        try
        {
            await run;
        }
        catch (OperationCanceledException) { }
    }

    private static IGatewayServer Gateway(ServerType transport)
    {
        var factory = new FakeBackendFactory(new SilentInstrument());
        return transport switch
        {
            ServerType.HiSlip => new HiSlipGatewayServer(
                factory,
                NullLogger<HiSlipGatewayServer>.Instance
            ),
            ServerType.Vxi11 => new Vxi11GatewayServer(
                factory,
                NullLogger<Vxi11GatewayServer>.Instance
            )
            {
                PortmapUdpPort = LoopbackGateway.FreePort(),
            },
            _ => new SocketGatewayServer(factory, NullLogger<SocketGatewayServer>.Instance),
        };
    }

    private static (IviCli.Domain.Servers.Server Server, ConfigDocument Config) Topology(
        ServerType transport
    )
    {
        var port = LoopbackGateway.FreePort();
        var deviceName = DeviceName.From("dut").ShouldBeOk();
        var device = new Device(
            deviceName,
            VisaResource.Parse("TCPIP0::127.0.0.1::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );
        var server = new IviCli.Domain.Servers.Server(
            ServerName.From("srv").ShouldBeOk(),
            transport,
            IpAddress.From("127.0.0.1").ShouldBeOk(),
            Port.From(port).ShouldBeOk()
        );
        var endpoint = transport switch
        {
            ServerType.HiSlip => "hislip0",
            ServerType.Vxi11 => "inst0",
            _ => "socket0",
        };
        var config = ConfigDocument
            .Empty.AddDevice(device)
            .ShouldBeOk()
            .AddServer(server)
            .ShouldBeOk()
            .AddRoute(
                new Route(server.Name, PublicEndpoint.From(endpoint).ShouldBeOk(), deviceName)
            )
            .ShouldBeOk();
        return (server, config);
    }

    private static (IIviBackend Client, Device Device) Client(ServerType transport, int port)
    {
        var (client, resource) = transport switch
        {
            ServerType.HiSlip => (
                (IIviBackend)new HiSlipBackend(port),
                $"TCPIP0::127.0.0.1::hislip0,{port}::INSTR"
            ),
            ServerType.Vxi11 => (new Vxi11Backend(port), $"TCPIP0::127.0.0.1::inst0,{port}::INSTR"),
            _ => (new SocketBackend(), $"TCPIP0::127.0.0.1::{port}::SOCKET"),
        };
        var device = new Device(
            DeviceName.From("remote").ShouldBeOk(),
            VisaResource.Parse(resource).ShouldBeOk(),
            Timeout.FromMilliseconds((int)DeviceTimeout.TotalMilliseconds).ShouldBeOk()
        );
        var bounded = new DeviceTimeoutBackendFactory(
            new FakeBackendFactory(client),
            TimeProvider.System
        )
            .CreateFor(device)
            .ShouldBeOk();
        return (bounded, device);
    }

    /// <summary>An instrument whose every query times out at once.</summary>
    private sealed class TimingOutInstrument : IIviBackend
    {
        private static Task<Result<T, BackendError>> TimeOut<T>() =>
            Task.FromResult(
                Result.Failure<T, BackendError>(
                    new TransportTimeout(TimeSpan.FromMilliseconds(300))
                )
            );

        public Task<Result<Unit, BackendError>> OpenAsync(Device device, CancellationToken ct) =>
            Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

        public Task<Result<Unit, BackendError>> CloseAsync(Device device, CancellationToken ct) =>
            Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

        public Task<Result<Unit, BackendError>> WriteAsync(
            Device device,
            ScpiCommand command,
            CancellationToken ct
        ) => TimeOut<Unit>();

        public Task<Result<string, BackendError>> QueryAsync(
            Device device,
            ScpiQuery query,
            CancellationToken ct
        ) => TimeOut<string>();

        public Task<Result<string, BackendError>> ReadAsync(Device device, CancellationToken ct) =>
            TimeOut<string>();

        public Task<Result<Unit, BackendError>> TriggerAsync(Device device, CancellationToken ct) =>
            TimeOut<Unit>();

        public async IAsyncEnumerable<ServiceRequest> ServiceRequestStream(
            Device device,
            [EnumeratorCancellation] CancellationToken ct
        )
        {
            await Task.Delay(System.Threading.Timeout.Infinite, ct);
            yield break;
        }
    }

    /// <summary>An instrument that accepts every command and answers no query.</summary>
    private sealed class SilentInstrument : IIviBackend
    {
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
            await Task.Delay(System.Threading.Timeout.Infinite, ct);
            return Result.Success<string, BackendError>("unreachable");
        }

        public async Task<Result<string, BackendError>> ReadAsync(
            Device device,
            CancellationToken ct
        )
        {
            await Task.Delay(System.Threading.Timeout.Infinite, ct);
            return Result.Success<string, BackendError>("unreachable");
        }

        public Task<Result<Unit, BackendError>> TriggerAsync(Device device, CancellationToken ct) =>
            Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

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
