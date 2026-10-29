using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Text;
using IviCli.Backends.Fake;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;
using IviCli.Domain.Protocols;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.Server.HiSlip;
using IviCli.TestKit;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace IviCli.Server.Tests;

/// <summary>
/// A backend failure reaches a HiSLIP client as a FatalError followed by
/// the connection closing, and the gateway log says why (ADR 0007 §9),
/// for a write as much as for a query. Every FatalError the sync channel
/// sends ends the connection, since the client has to initialize again.
/// </summary>
public sealed class HiSlipBackendFailureTests
{
    [Theory]
    [InlineData("OUTP OFF")]
    [InlineData("MEAS:CURR?")]
    public async Task Backend_failure_sends_FatalError_closes_and_logs_the_reason(string request)
    {
        // Given
        var logger = new RecordingLogger<HiSlipGatewayServer>();
        await using var gateway = await Gateway.StartAsync(logger);

        // When
        await SendDataEndAsync(gateway.Stream, request, gateway.Token);

        // Then
        await gateway.ShouldReceiveFatalErrorThenCloseAsync();
        logger.Entries.ShouldContain(e =>
            e.Level == LogLevel.Error && e.Message.Contains($"matches {request}")
        );
    }

    [Fact]
    public async Task Invalid_scpi_sends_FatalError_and_closes()
    {
        // Given
        await using var gateway = await Gateway.StartAsync(
            new RecordingLogger<HiSlipGatewayServer>()
        );

        // When
        await SendDataEndAsync(gateway.Stream, "OUTP\u0001ON", gateway.Token);

        // Then
        await gateway.ShouldReceiveFatalErrorThenCloseAsync();
    }

    private sealed class Gateway : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly Task _run;
        private readonly TcpClient _tcp;

        private Gateway(CancellationTokenSource cts, Task run, TcpClient tcp)
        {
            _cts = cts;
            _run = run;
            _tcp = tcp;
        }

        public NetworkStream Stream => _tcp.GetStream();

        public CancellationToken Token => _cts.Token;

        public static async Task<Gateway> StartAsync(RecordingLogger<HiSlipGatewayServer> logger)
        {
            var port = LoopbackGateway.FreePort();
            var deviceName = DeviceName.From("dut").ShouldBeOk();
            var device = new Device(
                deviceName,
                VisaResource.Parse("TCPIP0::127.0.0.1::hislip0::INSTR").ShouldBeOk(),
                Timeout.FromMilliseconds(3000).ShouldBeOk()
            );
            var serverName = ServerName.From("hislip-srv").ShouldBeOk();
            var server = new IviCli.Domain.Servers.Server(
                serverName,
                ServerType.HiSlip,
                IpAddress.From("127.0.0.1").ShouldBeOk(),
                Port.From(port).ShouldBeOk()
            );
            var config = ConfigDocument
                .Empty.AddDevice(device)
                .ShouldBeOk()
                .AddServer(server)
                .ShouldBeOk()
                .AddRoute(
                    new Route(serverName, PublicEndpoint.From("hislip0").ShouldBeOk(), deviceName)
                )
                .ShouldBeOk();
            var fake = new FakeBackend().ActivateScenario(
                MockScenario.SingleScene(
                    ScenarioName.From("strict").ShouldBeOk(),
                    idnDefault: null,
                    rules: ImmutableArray<MockRule>.Empty
                ) with
                {
                    Unmatched = UnmatchedPolicy.Fail,
                },
                deviceName
            );
            var gateway = new HiSlipGatewayServer(new FakeBackendFactory(fake), logger);
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            (port, var run) = LoopbackGateway.Start(
                server,
                s => gateway.RunAsync(s, config, cts.Token)
            );
            var tcp = await ConnectAsync(port, cts.Token);
            await InitializeAsync(tcp.GetStream(), cts.Token);
            return new Gateway(cts, run, tcp);
        }

        public async Task ShouldReceiveFatalErrorThenCloseAsync()
        {
            var reply = await ReadHeaderAsync(Stream, Token);
            reply.Type.ShouldBe(HiSlipMessageType.FatalError);
            await SkipAsync(Stream, reply.PayloadLength, Token);
            (await Stream.ReadAsync(new byte[1], Token)).ShouldBe(0);
        }

        public async ValueTask DisposeAsync()
        {
            _tcp.Dispose();
            await _cts.CancelAsync();
            try
            {
                await _run;
            }
            catch (OperationCanceledException) { }
            _cts.Dispose();
        }
    }

    private static async Task<TcpClient> ConnectAsync(int port, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var tcp = new TcpClient();
            try
            {
                await tcp.ConnectAsync(IPAddress.Loopback, port, ct);
                return tcp;
            }
            catch (SocketException)
            {
                tcp.Dispose();
                await Task.Delay(50, ct);
            }
        }
        throw new TimeoutException($"HiSLIP gateway did not bind to port {port}");
    }

    private static async Task InitializeAsync(NetworkStream stream, CancellationToken ct)
    {
        var payload = Encoding.ASCII.GetBytes("hislip0");
        var header = new byte[HiSlipMessage.HeaderSize];
        HiSlipMessage.WriteHeader(
            header,
            HiSlipMessageType.Initialize,
            controlCode: 0,
            messageParameter: 0,
            payloadLength: (ulong)payload.Length
        );
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(payload, ct);
        var response = await ReadHeaderAsync(stream, ct);
        response.Type.ShouldBe(HiSlipMessageType.InitializeResponse);
        await SkipAsync(stream, response.PayloadLength, ct);
    }

    private static async Task SendDataEndAsync(
        NetworkStream stream,
        string scpi,
        CancellationToken ct
    )
    {
        var bytes = Encoding.ASCII.GetBytes(scpi);
        var header = new byte[HiSlipMessage.HeaderSize];
        HiSlipMessage.WriteHeader(header, HiSlipMessageType.DataEnd, 0, 0, (ulong)bytes.Length);
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(bytes, ct);
    }

    private static async Task<HiSlipHeader> ReadHeaderAsync(
        NetworkStream stream,
        CancellationToken ct
    )
    {
        var buffer = new byte[HiSlipMessage.HeaderSize];
        await stream.ReadExactlyAsync(buffer, ct);
        return HiSlipMessage.ReadHeader(buffer);
    }

    private static async Task SkipAsync(NetworkStream stream, ulong length, CancellationToken ct)
    {
        if (length > 0)
        {
            await stream.ReadExactlyAsync(new byte[length], ct);
        }
    }
}
