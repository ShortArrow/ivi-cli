using System.Net;
using System.Net.Sockets;
using IviCli.Backends.Fake;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Protocols;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.Server.UsbIp;
using IviCli.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace IviCli.Server.Tests;

/// <summary>
/// One running USB/IP gateway over a free loopback port, plus the pieces
/// a test needs to talk to it. Every exported route names the profile its
/// device presents, so one server can carry a USBTMC export and a CDC-ACM
/// one at once, which is the shape ADR 0049 §5 describes.
/// </summary>
internal sealed class UsbIpBench : IAsyncDisposable
{
    /// <summary>The busid of the USBTMC export every bench has.</summary>
    public const string BusId = "1-1";

    /// <summary>The busid the CDC-ACM export takes when one is asked for.</summary>
    public const string CdcAcmBusId = "1-2";

    /// <summary>What the bound scenario answers <c>*IDN?</c> with.</summary>
    public const string IdnResponse = "FAKE,USBIP,0,1.0";

    /// <summary>The device every export is bound to unless one names another.</summary>
    public const string DefaultDeviceName = "dut";

    private readonly CancellationTokenSource _cts;
    private readonly Task _serverTask;
    private readonly int _port;
    private readonly List<UsbIpTestClient> _clients = [];
    private readonly IReadOnlyDictionary<string, Device> _devices;

    private UsbIpBench(
        CancellationTokenSource cts,
        Task serverTask,
        int port,
        FakeBackend backend,
        IReadOnlyDictionary<string, Device> devices
    )
    {
        _cts = cts;
        _serverTask = serverTask;
        _port = port;
        Backend = backend;
        _devices = devices;
    }

    public FakeBackend Backend { get; }

    /// <summary>The device of a bench whose exports all share one.</summary>
    public Device Device => DeviceNamed(DefaultDeviceName);

    public Device DeviceNamed(string name) => _devices[name];

    public CancellationToken Token => _cts.Token;

    /// <summary>A gateway with one USBTMC export at <see cref="BusId"/>.</summary>
    public static Task<UsbIpBench> StartAsync() => StartAsync((BusId, UsbExportProfile.UsbTmc));

    /// <summary>A gateway with one CDC-ACM export at <see cref="CdcAcmBusId"/>.</summary>
    public static Task<UsbIpBench> StartCdcAcmAsync() =>
        StartAsync((CdcAcmBusId, UsbExportProfile.CdcAcm));

    /// <summary>A gateway whose exports all speak for <see cref="DefaultDeviceName"/>.</summary>
    public static Task<UsbIpBench> StartAsync(
        params (string BusId, UsbExportProfile Profile)[] exports
    ) => StartAsync([.. exports.Select(e => (e.BusId, e.Profile, DefaultDeviceName))]);

    /// <summary>
    /// A gateway whose exports each name the device behind them, so a
    /// test can put two busids on two instruments rather than on one.
    /// </summary>
    public static async Task<UsbIpBench> StartAsync(
        params (string BusId, UsbExportProfile Profile, string DeviceName)[] exports
    )
    {
        var port = GetFreePort();
        var serverName = ServerName.From("usb-srv").ShouldBeOk();
        var server = new IviCli.Domain.Servers.Server(
            serverName,
            ServerType.UsbIp,
            IpAddress.From("127.0.0.1").ShouldBeOk(),
            Port.From(port).ShouldBeOk()
        );

        var devices = exports
            .Select(e => e.DeviceName)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(name => name, Define, StringComparer.Ordinal);

        var config = ConfigDocument.Empty.AddServer(server).ShouldBeOk();
        var backend = new FakeBackend();
        foreach (var device in devices.Values)
        {
            config = config.AddDevice(device).ShouldBeOk();
            backend.ConfigureDevice(device.Name, IdnResponse);
        }
        foreach (var (busId, profile, deviceName) in exports)
        {
            config = config
                .AddRoute(
                    new Route(
                        serverName,
                        PublicEndpoint.From(busId).ShouldBeOk(),
                        devices[deviceName].Name
                    )
                    {
                        Profile = profile,
                    }
                )
                .ShouldBeOk();
        }

        var gateway = new UsbIpGatewayServer(
            new FakeBackendFactory(backend),
            NullLogger<UsbIpGatewayServer>.Instance
        );

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);
        return new UsbIpBench(cts, serverTask, port, backend, devices);
    }

    private static Device Define(string name) =>
        new(
            DeviceName.From(name).ShouldBeOk(),
            VisaResource.Parse("TCPIP0::127.0.0.1::5025::SOCKET").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    public UsbIpTestClient Connect()
    {
        var client = new UsbIpTestClient(_port);
        _clients.Add(client);
        return client;
    }

    public Task<UsbIpTestClient> ImportAsync() => ImportAsync(BusId);

    public async Task<UsbIpTestClient> ImportAsync(string busId)
    {
        var client = Connect();
        var reply = await client.RequestImportAsync(busId, Token);
        reply.Status.ShouldBe(UsbIpConstants.StatusOk);
        return client;
    }

    /// <summary>
    /// Imports <paramref name="busId"/> once the attach that held it has
    /// let go. A detach is a closed socket, and the server releases the
    /// busid when the teardown behind that close finishes — nothing the
    /// client can observe on the wire — so the import is retried.
    /// </summary>
    public async Task<UsbIpTestClient> ImportWhenFreeAsync(string busId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var client = Connect();
            var reply = await client.RequestImportAsync(busId, Token);
            if (reply.Status == UsbIpConstants.StatusOk)
            {
                return client;
            }
            await Task.Delay(20, Token);
        }

        throw new TimeoutException($"busid {busId} was still refused after the attach ended");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

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

        throw new TimeoutException($"USB/IP gateway did not start listening on {port}");
    }
}
