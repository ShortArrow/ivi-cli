using System.Net;
using System.Net.Sockets;
using System.Text;
using IviCli.Application.Backends;
using IviCli.Backends.Fake;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.Server.Socket;
using IviCli.TestKit;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace IviCli.Server.Tests;

/// <summary>
/// A client that resets its connection (killed process, abortive close,
/// timeout-and-reconnect) ends the session like any other disconnect: the
/// gateway says so in one line and never reports it as an unexpected error.
/// </summary>
public sealed class SocketPeerAbortTests
{
    [Fact]
    public async Task Client_reset_while_idle_is_logged_as_a_disconnect_not_an_error()
    {
        await using var harness = await Harness.StartAsync();

        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, harness.ListenPort, harness.Token);
            await harness.WaitForSecondAsync("client connected");
            tcp.Client.Close(0);
        }

        await harness.WaitForSecondAsync("client disconnected");
        harness.Logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Error);
        harness.Logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Information && entry.Message.Contains("aborted")
        );
    }

    [Fact]
    public async Task Client_reset_after_a_query_is_logged_as_a_disconnect_not_an_error()
    {
        await using var harness = await Harness.StartAsync();

        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, harness.ListenPort, harness.Token);
            var stream = tcp.GetStream();
            await stream.WriteAsync(Encoding.UTF8.GetBytes("*IDN?\n"), harness.Token);
            var buffer = new byte[256];
            _ = await stream.ReadAsync(buffer, harness.Token);
            tcp.Client.Close(0);
        }

        await harness.WaitForSecondAsync("client disconnected");
        harness.Logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Client_leaving_with_responses_unread_is_logged_as_a_disconnect_not_an_error()
    {
        await using var harness = await Harness.StartAsync();

        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, harness.ListenPort, harness.Token);
            var burst = string.Concat(Enumerable.Repeat("*IDN?\n", 20000));
            await tcp.GetStream().WriteAsync(Encoding.UTF8.GetBytes(burst), harness.Token);
        }

        await harness.WaitForSecondAsync("client disconnected");
        harness.Logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Error);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly Task _serverTask;

        private Harness(
            int port,
            RecordingLogger<SocketGatewayServer> logger,
            CancellationTokenSource cts,
            Task serverTask
        )
        {
            ListenPort = port;
            Logger = logger;
            _cts = cts;
            _serverTask = serverTask;
        }

        public int ListenPort { get; }

        public RecordingLogger<SocketGatewayServer> Logger { get; }

        public CancellationToken Token => _cts.Token;

        public static async Task<Harness> StartAsync()
        {
            var port = GetFreePort();
            var deviceName = DeviceName.From("dut").ShouldBeOk();
            var device = new Device(
                deviceName,
                VisaResource.Parse("TCPIP0::127.0.0.1::5025::SOCKET").ShouldBeOk(),
                Timeout.FromMilliseconds(3000).ShouldBeOk()
            );
            var serverName = ServerName.From("socket-srv").ShouldBeOk();
            var config = ConfigDocument
                .Empty.AddDevice(device)
                .ShouldBeOk()
                .AddServer(
                    new IviCli.Domain.Servers.Server(
                        serverName,
                        ServerType.Socket,
                        IpAddress.From("127.0.0.1").ShouldBeOk(),
                        Port.From(port).ShouldBeOk()
                    )
                )
                .ShouldBeOk()
                .AddRoute(
                    new Route(serverName, PublicEndpoint.From("socket0").ShouldBeOk(), deviceName)
                )
                .ShouldBeOk();

            var fake = new FakeBackend().ConfigureDevice(deviceName, "FAKE,SOCKET,0,1.0");
            var logger = new RecordingLogger<SocketGatewayServer>();
            var gateway = new SocketGatewayServer(new FakeBackendFactory(fake), logger);

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var serverTask = gateway.RunAsync(config.Servers.Single(), config, cts.Token);
            await WaitForListenerAsync(port, cts.Token);
            await WaitForMessageAsync(logger, "client disconnected", 1, cts.Token);
            return new Harness(port, logger, cts, serverTask);
        }

        /// <summary>
        /// Waits for the test's own connection to produce <paramref name="text"/>;
        /// the listener probe in <see cref="StartAsync"/> produced the first one.
        /// </summary>
        public Task WaitForSecondAsync(string text) => WaitForMessageAsync(Logger, text, 2, Token);

        private static async Task WaitForMessageAsync(
            RecordingLogger<SocketGatewayServer> logger,
            string text,
            int occurrences,
            CancellationToken ct
        )
        {
            while (logger.Entries.Count(e => e.Message.Contains(text)) < occurrences)
            {
                await Task.Delay(20, ct);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            try
            {
                await _serverTask;
            }
            catch (OperationCanceledException) { }
            _cts.Dispose();
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
            for (var attempt = 0; attempt < 100; attempt++)
            {
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

            throw new TimeoutException($"gateway did not start listening on {port}");
        }
    }
}
