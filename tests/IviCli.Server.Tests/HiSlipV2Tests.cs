using System.Net;
using System.Net.Sockets;
using IviCli.Backends.Fake;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Protocols;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.Server.HiSlip;
using IviCli.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace IviCli.Server.Tests;

/// <summary>
/// HiSLIP v2 (ADR 0007 §1.5) scenario tests: lock acquire/release/contention
/// and async device clear acknowledge, driven over the async channel using
/// the raw HiSlipMessage framer.
/// </summary>
public sealed class HiSlipV2Tests
{
    [Fact]
    public async Task Async_lock_is_granted_and_then_released()
    {
        await using var harness = await Harness.StartAsync();

        await harness.OpenAsyncChannelAsync(sessionId: 1);
        var resp = await harness.SendAsync(HiSlipMessageType.AsyncLock, controlCode: 1);
        resp.Type.ShouldBe(HiSlipMessageType.AsyncLockResponse);
        resp.ControlCode.ShouldBe<byte>(1); // granted

        // Per IVI-6.1, release is AsyncLock with control byte 0.
        var release = await harness.SendAsync(HiSlipMessageType.AsyncLock, controlCode: 0);
        release.ControlCode.ShouldBe<byte>(1); // release always succeeds
    }

    [Fact]
    public async Task Second_session_lock_is_denied_when_first_holds_it()
    {
        await using var harness = await Harness.StartAsync();

        await harness.OpenAsyncChannelAsync(sessionId: 1);
        var firstGrant = await harness.SendAsync(HiSlipMessageType.AsyncLock, controlCode: 1);
        firstGrant.ControlCode.ShouldBe<byte>(1);

        // Second session connects on a fresh async channel.
        await using var second = await harness.OpenSecondAsyncChannelAsync(sessionId: 2);
        var denial = await second.SendAsync(HiSlipMessageType.AsyncLock, controlCode: 1);
        denial.Type.ShouldBe(HiSlipMessageType.AsyncLockResponse);
        denial.ControlCode.ShouldBe<byte>(0); // denied
    }

    [Fact]
    public async Task Async_device_clear_is_acknowledged()
    {
        await using var harness = await Harness.StartAsync();

        await harness.OpenAsyncChannelAsync(sessionId: 1);
        var resp = await harness.SendAsync(HiSlipMessageType.AsyncDeviceClear);
        resp.Type.ShouldBe(HiSlipMessageType.AsyncDeviceClearAcknowledge);
    }

    [Fact]
    public async Task Released_lock_can_be_reacquired_by_another_session()
    {
        await using var harness = await Harness.StartAsync();

        await harness.OpenAsyncChannelAsync(sessionId: 1);
        var grant1 = await harness.SendAsync(HiSlipMessageType.AsyncLock, controlCode: 1);
        grant1.ControlCode.ShouldBe<byte>(1);

        // Send release via control byte 0 (which is one of the two release
        // signals supported by the server implementation).
        var release = await harness.SendAsync(HiSlipMessageType.AsyncLock, controlCode: 0);
        release.ControlCode.ShouldBe<byte>(1); // release always succeeds

        await using var second = await harness.OpenSecondAsyncChannelAsync(sessionId: 2);
        var grant2 = await second.SendAsync(HiSlipMessageType.AsyncLock, controlCode: 1);
        grant2.ControlCode.ShouldBe<byte>(1);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly int _port;
        private readonly HiSlipGatewayServer _gateway;
        private readonly CancellationTokenSource _cts;
        private readonly Task _serverTask;
        private TcpClient? _async;
        private NetworkStream? _asyncStream;

        private Harness(
            int port,
            HiSlipGatewayServer gateway,
            CancellationTokenSource cts,
            Task serverTask
        )
        {
            _port = port;
            _gateway = gateway;
            _cts = cts;
            _serverTask = serverTask;
        }

        public static async Task<Harness> StartAsync()
        {
            var port = GetFreePort();
            var deviceName = DeviceName.From("dut").ShouldBeOk();
            var device = new Device(
                deviceName,
                VisaResource.Parse("TCPIP0::127.0.0.1::hislip0::INSTR").ShouldBeOk(),
                Timeout.FromMilliseconds(3000).ShouldBeOk()
            );
            var serverName = ServerName.From("hislip-srv").ShouldBeOk();
            var endpoint = PublicEndpoint.From("dut").ShouldBeOk();
            var bind = IpAddress.From("127.0.0.1").ShouldBeOk();
            var portValue = Port.From(port).ShouldBeOk();
            var server = new IviCli.Domain.Servers.Server(
                serverName,
                ServerType.HiSlip,
                bind,
                portValue
            );
            var route = new Route(serverName, endpoint, deviceName);
            var config = ConfigDocument
                .Empty.AddDevice(device)
                .ShouldBeOk()
                .AddServer(server)
                .ShouldBeOk()
                .AddRoute(route)
                .ShouldBeOk();

            var fake = new FakeBackend().ConfigureDevice(deviceName, "FAKE,HISLIP,0,1.0");
            var gateway = new HiSlipGatewayServer(
                new FakeBackendFactory(fake),
                NullLogger<HiSlipGatewayServer>.Instance
            );
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var serverTask = gateway.RunAsync(server, config, cts.Token);
            await WaitForListenerAsync(port, cts.Token);
            return new Harness(port, gateway, cts, serverTask);
        }

        public async Task OpenAsyncChannelAsync(ushort sessionId)
        {
            _async = new TcpClient();
            await _async.ConnectAsync(IPAddress.Loopback, _port, _cts.Token);
            _asyncStream = _async.GetStream();
            await SendAsyncInitializeAsync(_asyncStream!, sessionId, _cts.Token);
        }

        public async Task<Harness> OpenSecondAsyncChannelAsync(ushort sessionId)
        {
            var clone = new Harness(_port, _gateway, _cts, _serverTask);
            await clone.OpenAsyncChannelAsync(sessionId);
            return clone;
        }

        public async Task<HiSlipHeader> SendAsync(
            HiSlipMessageType type,
            byte controlCode = 0,
            uint messageParameter = 0
        )
        {
            await SendOneWayAsync(type, controlCode, messageParameter);
            return await ReadHeaderAsync(_asyncStream!, _cts.Token);
        }

        public async Task SendOneWayAsync(
            HiSlipMessageType type,
            byte controlCode = 0,
            uint messageParameter = 0
        )
        {
            var header = new byte[HiSlipMessage.HeaderSize];
            HiSlipMessage.WriteHeader(header, type, controlCode, messageParameter, 0);
            await _asyncStream!.WriteAsync(header, _cts.Token);
        }

        private static async Task SendAsyncInitializeAsync(
            NetworkStream stream,
            ushort sessionId,
            CancellationToken ct
        )
        {
            var header = new byte[HiSlipMessage.HeaderSize];
            HiSlipMessage.WriteHeader(
                header,
                HiSlipMessageType.AsyncInitialize,
                controlCode: 0,
                messageParameter: sessionId,
                payloadLength: 0
            );
            await stream.WriteAsync(header, ct);

            // Read AsyncInitializeResponse so the server's read loop is past it.
            var response = await ReadHeaderAsync(stream, ct);
            response.Type.ShouldBe(HiSlipMessageType.AsyncInitializeResponse);
        }

        private static async Task<HiSlipHeader> ReadHeaderAsync(
            NetworkStream stream,
            CancellationToken ct
        )
        {
            var buf = new byte[HiSlipMessage.HeaderSize];
            await ReadExactlyAsync(stream, buf, ct);
            var header = HiSlipMessage.ReadHeader(buf);
            if (header.PayloadLength > 0)
            {
                var pl = new byte[header.PayloadLength];
                await ReadExactlyAsync(stream, pl, ct);
            }
            return header;
        }

        private static async Task ReadExactlyAsync(
            NetworkStream stream,
            byte[] buffer,
            CancellationToken ct
        )
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset), ct);
                if (read <= 0)
                {
                    throw new EndOfStreamException($"closed early at {offset}/{buffer.Length}");
                }
                offset += read;
            }
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static async Task WaitForListenerAsync(int port, CancellationToken ct)
        {
            for (var i = 0; i < 50; i++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var probe = new TcpClient();
                    await probe.ConnectAsync(IPAddress.Loopback, port, ct);
                    return;
                }
                catch (SocketException)
                {
                    await Task.Delay(50, ct);
                }
            }
            throw new TimeoutException($"HiSLIP gateway did not bind to port {port}");
        }

        public async ValueTask DisposeAsync()
        {
            _asyncStream?.Dispose();
            _async?.Dispose();
            // Only the original harness should cancel the server.
            if (!_cts.IsCancellationRequested)
            {
                try
                {
                    await _cts.CancelAsync();
                }
                catch
                { /* ignore */
                }
            }
            try
            {
                await _serverTask;
            }
            catch
            { /* ignore */
            }
        }
    }
}
