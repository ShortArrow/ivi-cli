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
/// The gateway logs why an operation failed, not just that it failed. Operators
/// reading gateway logs need to tell a lease wait ("another op is in flight",
/// warning) apart from a silent instrument (error); a fixed string at the call
/// site collapses both into one line.
/// </summary>
public sealed class SocketGatewayLogsErrorDetailTests
{
    [Fact]
    public async Task Malformed_scpi_is_logged_with_the_reason_it_was_rejected()
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
        var server = config.Servers.Single();

        var fake = new FakeBackend().ConfigureDevice(deviceName, "FAKE,SOCKET,0,1.0");
        var logger = new RecordingLogger();
        var gateway = new SocketGatewayServer(new FakeBackendFactory(fake), logger);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);

        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, port, cts.Token);
            await using var stream = tcp.GetStream();
            var overlong = new string('X', 5000) + "?\n";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(overlong), cts.Token);
            await logger.WaitForWarningAsync(cts.Token);
        }

        var warning = logger.Entries.First(entry => entry.Level == LogLevel.Warning);
        warning.Text.ShouldContain("invalid SCPI query");
        warning.Text.ShouldContain("exceeds", Case.Insensitive);

        await cts.CancelAsync();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException) { }
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

    private sealed class RecordingLogger : ILogger<SocketGatewayServer>
    {
        private readonly TaskCompletionSource _firstWarning = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public List<Entry> Entries { get; } = new();

        public Task WaitForWarningAsync(CancellationToken ct) => _firstWarning.Task.WaitAsync(ct);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            lock (Entries)
            {
                Entries.Add(new Entry(logLevel, formatter(state, exception)));
            }

            if (logLevel == LogLevel.Warning)
            {
                _firstWarning.TrySetResult();
            }
        }

        public sealed record Entry(LogLevel Level, string Text);
    }
}
