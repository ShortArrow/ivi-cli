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
/// A SOCKET gateway bound to a free loopback port over a Fake backend whose
/// device answers <c>*IDN?</c>, with every log entry recorded.
/// </summary>
internal sealed class SocketGatewayHarness : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly Task _serverTask;

    private SocketGatewayHarness(
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

    public static async Task<SocketGatewayHarness> StartAsync()
    {
        var port = LoopbackGateway.FreePort();
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
        (port, var serverTask) = LoopbackGateway.Start(
            config.Servers.Single(),
            s => gateway.RunAsync(s, config, cts.Token)
        );
        await WaitForListenerAsync(port, cts.Token);
        await WaitForMessageAsync(logger, "client disconnected", 1, cts.Token);
        return new SocketGatewayHarness(port, logger, cts, serverTask);
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
