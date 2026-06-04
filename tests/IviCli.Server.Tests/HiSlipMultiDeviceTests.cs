using System.Net;
using System.Net.Sockets;
using IviCli.Application.Backends;
using IviCli.Backends.Fake;
using IviCli.Backends.HiSlip;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.Server.HiSlip;
using IviCli.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace IviCli.Server.Tests;

/// <summary>
/// Locks in the v0.3.0 HiSlip sub-address multiplexing (#21): a single
/// gateway server with two routes (<c>hislip0</c> → psu, <c>hislip1</c>
/// → dmm) must serve both backends concurrently, with the right device
/// picked per session based on the client-supplied sub-address in the
/// Initialize payload (IVI-6.1 §10.2.1).
/// </summary>
public sealed class HiSlipMultiDeviceTests
{
    [Fact]
    public async Task Two_sub_addresses_on_one_server_route_to_distinct_devices()
    {
        var port = GetFreePort();

        var psu = new Device(
            DeviceName.From("psu").ShouldBeOk(),
            VisaResource.Parse("TCPIP0::127.0.0.1::hislip0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );
        var dmm = new Device(
            DeviceName.From("dmm").ShouldBeOk(),
            VisaResource.Parse("TCPIP0::127.0.0.1::hislip1::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

        var serverName = ServerName.From("hislip-srv").ShouldBeOk();
        var server = new IviCli.Domain.Servers.Server(
            serverName,
            ServerType.HiSlip,
            IpAddress.From("127.0.0.1").ShouldBeOk(),
            Port.From(port).ShouldBeOk()
        );

        var routePsu = new Route(serverName, PublicEndpoint.From("hislip0").ShouldBeOk(), psu.Name);
        var routeDmm = new Route(serverName, PublicEndpoint.From("hislip1").ShouldBeOk(), dmm.Name);

        var config = ConfigDocument
            .Empty.AddDevice(psu)
            .ShouldBeOk()
            .AddDevice(dmm)
            .ShouldBeOk()
            .AddServer(server)
            .ShouldBeOk()
            .AddRoute(routePsu)
            .ShouldBeOk()
            .AddRoute(routeDmm)
            .ShouldBeOk();

        var fake = new FakeBackend()
            .RespondToQuery(psu.Name, "*IDN?", "ACME,PSU,1,1.0")
            .RespondToQuery(dmm.Name, "*IDN?", "ACME,DMM,1,1.0");

        var gateway = new HiSlipGatewayServer(
            new FakeBackendFactory(fake),
            NullLogger<HiSlipGatewayServer>.Instance
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);

        await WaitForListenerAsync(port, cts.Token);

        // The HiSlipBackend client emits its TCPIP resource's LanDevice
        // segment as the Initialize payload, so connecting to the same
        // gateway port with two different LanDevice strings should
        // resolve to two different routes/devices.
        var psuClient = new HiSlipBackend(port);
        var dmmClient = new HiSlipBackend(port);

        (await psuClient.OpenAsync(psu, cts.Token)).ShouldBeOk();
        (await dmmClient.OpenAsync(dmm, cts.Token)).ShouldBeOk();

        var psuIdn = await psuClient.QueryAsync(
            psu,
            ScpiQuery.From("*IDN?").ShouldBeOk(),
            cts.Token
        );
        psuIdn.ShouldBeOk().ShouldBe("ACME,PSU,1,1.0");

        var dmmIdn = await dmmClient.QueryAsync(
            dmm,
            ScpiQuery.From("*IDN?").ShouldBeOk(),
            cts.Token
        );
        dmmIdn.ShouldBeOk().ShouldBe("ACME,DMM,1,1.0");

        await psuClient.CloseAsync(psu, cts.Token);
        await dmmClient.CloseAsync(dmm, cts.Token);
        await cts.CancelAsync();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task Sub_address_with_no_matching_route_returns_fatal_error()
    {
        var port = GetFreePort();

        var psu = new Device(
            DeviceName.From("psu").ShouldBeOk(),
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
        var routePsu = new Route(serverName, PublicEndpoint.From("hislip0").ShouldBeOk(), psu.Name);
        var config = ConfigDocument
            .Empty.AddDevice(psu)
            .ShouldBeOk()
            .AddServer(server)
            .ShouldBeOk()
            .AddRoute(routePsu)
            .ShouldBeOk();

        var fake = new FakeBackend().RespondToQuery(psu.Name, "*IDN?", "ACME,PSU,1,1.0");
        var gateway = new HiSlipGatewayServer(
            new FakeBackendFactory(fake),
            NullLogger<HiSlipGatewayServer>.Instance
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);

        await WaitForListenerAsync(port, cts.Token);

        // Client requests sub-address `hislip9` which is not bound; the
        // gateway must surface a Fatal error rather than silently
        // serving the first available route.
        var phantom = new Device(
            DeviceName.From("phantom").ShouldBeOk(),
            VisaResource.Parse("TCPIP0::127.0.0.1::hislip9::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );
        var phantomClient = new HiSlipBackend(port);

        var openResult = await phantomClient.OpenAsync(phantom, cts.Token);
        openResult.ShouldBeError();

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
}
