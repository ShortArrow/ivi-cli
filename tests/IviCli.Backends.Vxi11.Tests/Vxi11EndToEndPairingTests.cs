using System.Net;
using System.Net.Sockets;
using IviCli.Application.Backends;
using IviCli.Backends.Fake;
using IviCli.Backends.Vxi11;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.Server.Vxi11;
using IviCli.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace IviCli.Backends.Vxi11.Tests;

/// <summary>
/// Server↔client pairing: drive <see cref="Vxi11Backend"/> against an
/// in-proc <see cref="Vxi11GatewayServer"/> backed by <see cref="FakeBackend"/>.
/// Protects both halves of the wire contract from a single test surface
/// without external prereqs (no PyVISA dependency).
/// </summary>
public sealed class Vxi11EndToEndPairingTests
{
    [Fact]
    public async Task QueryAsync_roundtrips_through_gateway_to_fake_backend()
    {
        var (gateway, server, config, port, fake) = BuildHarness();
        var deviceName = DeviceName.From("dut").ShouldBeOk();
        fake.RespondToQuery(deviceName, "*IDN?", "FAKE,VXI11,IVI-CLI,1.0");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);

        var backend = new Vxi11Backend(port);
        var device = config.FindDevice(deviceName).ShouldNotBeNull();
        (await backend.OpenAsync(device, cts.Token)).ShouldBeOk();

        var query = ScpiQuery.From("*IDN?").ShouldBeOk();
        var response = await backend.QueryAsync(device, query, cts.Token);
        response.ShouldBeOk().ShouldBe("FAKE,VXI11,IVI-CLI,1.0");

        (await backend.CloseAsync(device, cts.Token)).ShouldBeOk();
        await cts.CancelAsync();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task WriteAsync_succeeds_for_non_query_command()
    {
        var (gateway, server, config, port, fake) = BuildHarness();
        var deviceName = DeviceName.From("dut").ShouldBeOk();
        _ = fake;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);

        var backend = new Vxi11Backend(port);
        var device = config.FindDevice(deviceName).ShouldNotBeNull();
        (await backend.OpenAsync(device, cts.Token)).ShouldBeOk();

        var command = ScpiCommand.From("OUTP ON").ShouldBeOk();
        var result = await backend.WriteAsync(device, command, cts.Token);
        result.ShouldBeOk();

        (await backend.CloseAsync(device, cts.Token)).ShouldBeOk();
        await cts.CancelAsync();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task Close_then_open_reuses_the_backend_instance_cleanly()
    {
        var (gateway, server, config, port, fake) = BuildHarness();
        var deviceName = DeviceName.From("dut").ShouldBeOk();
        fake.RespondToQuery(deviceName, "*IDN?", "FAKE,VXI11,IVI-CLI,1.0");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);

        var backend = new Vxi11Backend(port);
        var device = config.FindDevice(deviceName).ShouldNotBeNull();
        (await backend.OpenAsync(device, cts.Token)).ShouldBeOk();
        (await backend.CloseAsync(device, cts.Token)).ShouldBeOk();
        (await backend.OpenAsync(device, cts.Token)).ShouldBeOk();
        var query = ScpiQuery.From("*IDN?").ShouldBeOk();
        (await backend.QueryAsync(device, query, cts.Token))
            .ShouldBeOk()
            .ShouldBe("FAKE,VXI11,IVI-CLI,1.0");
        (await backend.CloseAsync(device, cts.Token)).ShouldBeOk();

        await cts.CancelAsync();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException) { }
    }

    private static (
        Vxi11GatewayServer Gateway,
        IviCli.Domain.Servers.Server Server,
        ConfigDocument Config,
        int Port,
        FakeBackend Fake
    ) BuildHarness()
    {
        var port = GetFreePort();
        var deviceName = DeviceName.From("dut").ShouldBeOk();
        var device = new Device(
            deviceName,
            VisaResource.Parse("TCPIP0::127.0.0.1::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );
        var serverName = ServerName.From("vxi-srv").ShouldBeOk();
        var endpoint = PublicEndpoint.From("inst0").ShouldBeOk();
        var bind = IpAddress.From("127.0.0.1").ShouldBeOk();
        var portValue = Port.From(port).ShouldBeOk();
        var srv = new IviCli.Domain.Servers.Server(serverName, ServerType.Vxi11, bind, portValue);
        var route = new Route(serverName, endpoint, deviceName);
        var config = ConfigDocument
            .Empty.AddDevice(device)
            .ShouldBeOk()
            .AddServer(srv)
            .ShouldBeOk()
            .AddRoute(route)
            .ShouldBeOk();
        var fake = new FakeBackend().ConfigureDevice(deviceName, "FAKE,VXI11,IVI-CLI,1.0");
        var gateway = new Vxi11GatewayServer(
            new FakeBackendFactory(fake),
            NullLogger<Vxi11GatewayServer>.Instance
        );
        return (gateway, srv, config, port, fake);
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
        throw new TimeoutException($"VXI-11 gateway did not bind to port {port}");
    }
}
