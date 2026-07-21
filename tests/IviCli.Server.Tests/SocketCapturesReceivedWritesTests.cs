using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using IviCli.Application.Backends;
using IviCli.Application.Capture;
using IviCli.Backends.Fake;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.Server.Socket;
using IviCli.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace IviCli.Server.Tests;

/// <summary>
/// Closes the requirement-2 seam: a client write that reaches the SOCKET
/// gateway is captured as a <see cref="TrafficOp.Write"/> event (with the exact
/// SCPI) when the backend factory is wrapped for capture — the substrate the
/// out-of-band <c>mock received</c> query then reads back.
/// </summary>
public sealed class SocketCapturesReceivedWritesTests
{
    [Fact]
    public async Task Client_write_through_the_gateway_is_captured_with_its_scpi()
    {
        var port = GetFreePort();
        var deviceName = DeviceName.From("dut").ShouldBeOk();
        var device = new Device(
            deviceName,
            VisaResource.Parse("TCPIP0::127.0.0.1::5025::SOCKET").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );
        var serverName = ServerName.From("socket-srv").ShouldBeOk();
        var endpoint = PublicEndpoint.From("socket0").ShouldBeOk();
        var bind = IpAddress.From("127.0.0.1").ShouldBeOk();
        var portValue = Port.From(port).ShouldBeOk();
        var server = new IviCli.Domain.Servers.Server(
            serverName,
            ServerType.Socket,
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

        var fake = new FakeBackend().ConfigureDevice(deviceName, "FAKE,SOCKET,0,1.0");
        var recorder = new RecordingTrafficWriter();
        var factory = new CapturingBackendFactory(new FakeBackendFactory(fake), recorder);
        var gateway = new SocketGatewayServer(factory, NullLogger<SocketGatewayServer>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);

        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, port, cts.Token);
            await using var stream = tcp.GetStream();
            var bytes = Encoding.UTF8.GetBytes(":VOLT 24.000\n");
            await stream.WriteAsync(bytes, cts.Token);
            // Let the server-side read loop dispatch + capture the write.
            await recorder.WaitForWriteAsync(cts.Token);
        }

        var writes = recorder.Events.Where(e => e.Op == TrafficOp.Write).ToList();
        writes.ShouldContain(e => e.Device == "dut" && e.Data == ":VOLT 24.000");

        await cts.CancelAsync();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException) { }
    }

    private sealed class RecordingTrafficWriter : ITrafficWriter
    {
        public ConcurrentQueue<TrafficEvent> Events { get; } = new();
        private readonly TaskCompletionSource _firstWrite = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task AppendAsync(TrafficEvent ev, CancellationToken ct)
        {
            Events.Enqueue(ev);
            if (ev.Op == TrafficOp.Write)
            {
                _firstWrite.TrySetResult();
            }
            return Task.CompletedTask;
        }

        public async Task WaitForWriteAsync(CancellationToken ct)
        {
            using var reg = ct.Register(() => _firstWrite.TrySetCanceled(ct));
            await _firstWrite.Task;
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
        throw new TimeoutException($"SOCKET gateway did not bind to port {port}");
    }
}
