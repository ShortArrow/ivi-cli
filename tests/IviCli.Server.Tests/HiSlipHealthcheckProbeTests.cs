using System.Net;
using System.Net.Sockets;
using IviCli.Backends.Fake;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.Server.HiSlip;
using IviCli.TestKit;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace IviCli.Server.Tests;

/// <summary>
/// Verifies that a TCP probe (the shape Docker HEALTHCHECK uses —
/// `nc -z` opens then immediately closes the connection) does
/// <b>not</b> produce a <see cref="LogLevel.Error"/> entry in the
/// HiSlip gateway logs (Batch X §3). Early-handshake disconnects
/// land at <see cref="LogLevel.Debug"/> instead.
/// </summary>
public sealed class HiSlipHealthcheckProbeTests
{
    [Fact]
    public async Task TCP_probe_does_not_emit_LogError()
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
            .AddRoute(new Route(serverName, endpoint, deviceName))
            .ShouldBeOk();

        var capturedLogger = new CapturingLogger();
        var gateway = new HiSlipGatewayServer(
            new FakeBackendFactory(new FakeBackend()),
            capturedLogger
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);

        await WaitForListenerAsync(port, cts.Token);

        // Simulate a healthcheck probe: connect, immediately close.
        // Repeat a few times for stability.
        for (var i = 0; i < 3; i++)
        {
            using var probe = new TcpClient();
            await probe.ConnectAsync(IPAddress.Loopback, port, cts.Token);
            probe.Close();
        }

        // Let the server-side handlers settle.
        await Task.Delay(150, cts.Token);

        await cts.CancelAsync();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException) { }

        capturedLogger.ErrorMessages.ShouldBeEmpty(
            "TCP probe disconnects must not produce LogError entries"
        );
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

    private sealed class CapturingLogger : ILogger<HiSlipGatewayServer>
    {
        public List<string> ErrorMessages { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel >= LogLevel.Error)
            {
                lock (ErrorMessages)
                {
                    ErrorMessages.Add(formatter(state, exception));
                }
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose() { }
        }
    }
}
