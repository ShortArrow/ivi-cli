using System.Net;
using System.Net.Sockets;
using System.Text;
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
/// HiSLIP v3 (ADR 0007 §1.5) scenarios:
/// <c>lock_timeout</c> honoured on AsyncLock (acquire) and Trigger
/// accepted on the sync channel without producing FatalError.
/// </summary>
public sealed class HiSlipV3Tests
{
    [Fact]
    public async Task Lock_timeout_zero_under_contention_denies_immediately()
    {
        await using var harness = await Harness.StartAsync();
        await harness.OpenAsyncChannelAsync(sessionId: 1);
        (
            await harness.SendAsync(HiSlipMessageType.AsyncLock, controlCode: 1)
        ).ControlCode.ShouldBe<byte>(1);

        await using var second = await harness.OpenSecondAsyncChannelAsync(sessionId: 2);
        var started = DateTime.UtcNow;
        var denial = await second.SendAsync(
            HiSlipMessageType.AsyncLock,
            controlCode: 1,
            messageParameter: 0
        );

        denial.ControlCode.ShouldBe<byte>(0);
        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public async Task Lock_timeout_expires_when_holder_keeps_lock()
    {
        await using var harness = await Harness.StartAsync();
        await harness.OpenAsyncChannelAsync(sessionId: 1);
        (
            await harness.SendAsync(HiSlipMessageType.AsyncLock, controlCode: 1)
        ).ControlCode.ShouldBe<byte>(1);

        await using var second = await harness.OpenSecondAsyncChannelAsync(sessionId: 2);
        var started = DateTime.UtcNow;
        var denial = await second.SendAsync(
            HiSlipMessageType.AsyncLock,
            controlCode: 1,
            messageParameter: 300
        );
        var elapsed = DateTime.UtcNow - started;

        denial.ControlCode.ShouldBe<byte>(0);
        elapsed.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public async Task Lock_timeout_grants_when_holder_releases_before_deadline()
    {
        await using var harness = await Harness.StartAsync();
        await harness.OpenAsyncChannelAsync(sessionId: 1);
        (
            await harness.SendAsync(HiSlipMessageType.AsyncLock, controlCode: 1)
        ).ControlCode.ShouldBe<byte>(1);
        await using var second = await harness.OpenSecondAsyncChannelAsync(sessionId: 2);

        // Schedule a release on the holder ~100ms in.
        var releaseTask = Task.Run(async () =>
        {
            await Task.Delay(100);
            await harness.SendAsync(HiSlipMessageType.AsyncLock, controlCode: 0);
        });

        var grant = await second.SendAsync(
            HiSlipMessageType.AsyncLock,
            controlCode: 1,
            messageParameter: 2000
        );

        grant.ControlCode.ShouldBe<byte>(1);
        await releaseTask;
    }

    [Fact]
    public async Task Trigger_on_sync_channel_is_accepted_without_FatalError()
    {
        await using var harness = await Harness.StartAsync();
        var sync = await harness.OpenSyncChannelAsync();
        await harness.SendSyncAsync(sync, HiSlipMessageType.Trigger);
        // Drive a normal query after Trigger to assert the sync channel
        // is still operational (would be torn down on FatalError).
        await harness.SendSyncQueryAsync(sync, "*IDN?");
        var response = await harness.ReadSyncDataEndAsync(sync);
        response.ShouldBe("FAKE,HISLIP,0,1.0");
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly int _port;
        private readonly CancellationTokenSource _cts;
        private readonly Task _serverTask;
        private TcpClient? _async;
        private NetworkStream? _asyncStream;

        private Harness(int port, CancellationTokenSource cts, Task serverTask)
        {
            _port = port;
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
            var endpoint = PublicEndpoint.From("hislip0").ShouldBeOk();
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

            var fake = new FakeBackend()
                .ConfigureDevice(deviceName, "FAKE,HISLIP,0,1.0")
                .RespondToQuery(deviceName, "*IDN?", "FAKE,HISLIP,0,1.0");
            var gateway = new HiSlipGatewayServer(
                new FakeBackendFactory(fake),
                NullLogger<HiSlipGatewayServer>.Instance
            );
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var serverTask = gateway.RunAsync(server, config, cts.Token);
            await WaitForListenerAsync(port, cts.Token);
            return new Harness(port, cts, serverTask);
        }

        public async Task OpenAsyncChannelAsync(ushort sessionId)
        {
            _async = new TcpClient();
            await _async.ConnectAsync(IPAddress.Loopback, _port, _cts.Token);
            _asyncStream = _async.GetStream();
            var header = new byte[HiSlipMessage.HeaderSize];
            HiSlipMessage.WriteHeader(
                header,
                HiSlipMessageType.AsyncInitialize,
                controlCode: 0,
                messageParameter: sessionId,
                payloadLength: 0
            );
            await _asyncStream.WriteAsync(header, _cts.Token);
            var resp = await ReadHeaderAsync(_asyncStream, _cts.Token);
            resp.Type.ShouldBe(HiSlipMessageType.AsyncInitializeResponse);
        }

        public async Task<Harness> OpenSecondAsyncChannelAsync(ushort sessionId)
        {
            var clone = new Harness(_port, _cts, _serverTask);
            await clone.OpenAsyncChannelAsync(sessionId);
            return clone;
        }

        public async Task<HiSlipHeader> SendAsync(
            HiSlipMessageType type,
            byte controlCode = 0,
            uint messageParameter = 0
        )
        {
            var header = new byte[HiSlipMessage.HeaderSize];
            HiSlipMessage.WriteHeader(header, type, controlCode, messageParameter, 0);
            await _asyncStream!.WriteAsync(header, _cts.Token);
            return await ReadHeaderAsync(_asyncStream, _cts.Token);
        }

        public async Task<NetworkStream> OpenSyncChannelAsync()
        {
            var sync = new TcpClient();
            await sync.ConnectAsync(IPAddress.Loopback, _port, _cts.Token);
            var stream = sync.GetStream();
            // Initialize payload carries the sub-address per IVI-6.1
            // §10.2.1 — must match the route's endpoint (issue #21).
            var payload = System.Text.Encoding.ASCII.GetBytes("hislip0");
            var header = new byte[HiSlipMessage.HeaderSize];
            HiSlipMessage.WriteHeader(
                header,
                HiSlipMessageType.Initialize,
                controlCode: 0,
                messageParameter: 0,
                payloadLength: (ulong)payload.Length
            );
            await stream.WriteAsync(header, _cts.Token);
            await stream.WriteAsync(payload, _cts.Token);
            var resp = await ReadHeaderAsync(stream, _cts.Token);
            resp.Type.ShouldBe(HiSlipMessageType.InitializeResponse);
            return stream;
        }

        public async Task SendSyncAsync(NetworkStream stream, HiSlipMessageType type)
        {
            var header = new byte[HiSlipMessage.HeaderSize];
            HiSlipMessage.WriteHeader(header, type, 0, 0, 0);
            await stream.WriteAsync(header, _cts.Token);
        }

        public async Task SendSyncQueryAsync(NetworkStream stream, string scpi)
        {
            var bytes = Encoding.ASCII.GetBytes(scpi);
            var header = new byte[HiSlipMessage.HeaderSize];
            HiSlipMessage.WriteHeader(header, HiSlipMessageType.DataEnd, 0, 0, (ulong)bytes.Length);
            await stream.WriteAsync(header, _cts.Token);
            await stream.WriteAsync(bytes, _cts.Token);
        }

        public async Task<string> ReadSyncDataEndAsync(NetworkStream stream)
        {
            var buf = new byte[HiSlipMessage.HeaderSize];
            await ReadExactlyAsync(stream, buf, _cts.Token);
            var header = HiSlipMessage.ReadHeader(buf);
            header.Type.ShouldBe(HiSlipMessageType.DataEnd);
            var payload = new byte[header.PayloadLength];
            if (payload.Length > 0)
            {
                await ReadExactlyAsync(stream, payload, _cts.Token);
            }
            return Encoding.ASCII.GetString(payload);
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
