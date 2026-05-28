using System.Net;
using System.Net.Sockets;
using IviCli.Application.Backends;
using IviCli.Backends.Fake;
using IviCli.Backends.Vxi11;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.Server.Vxi11;
using IviCli.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace IviCli.Server.Tests;

/// <summary>
/// End-to-end VXI-11 Interrupt channel tests (ADR 0042) using the
/// real <see cref="Vxi11Backend"/> client + <see cref="Vxi11GatewayServer"/>
/// gateway over loopback TCP. The Fake backend's
/// <c>RaiseServiceRequest</c> drives the gateway forwarder, which
/// TCP-connects back to the client's listening port and delivers a
/// <c>device_intr_srq</c> the client decodes into its
/// <see cref="IIviBackend.ServiceRequestStream"/>.
/// </summary>
public sealed class Vxi11InterruptChannelTests
{
    [Fact]
    public async Task FakeBackend_RaiseServiceRequest_propagates_to_Vxi11Backend_stream()
    {
        var (gateway, server, config, port, fake, deviceName) = BuildHarness();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);

        var device = config.FindDevice(deviceName)!;
        var client = new Vxi11Backend(port);
        (await client.OpenAsync(device, cts.Token)).ShouldBeOk();

        var observed = new List<ServiceRequest>();
        var consumerCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        var consumer = Task.Run(async () =>
        {
            try
            {
                await foreach (var srq in client.ServiceRequestStream(device, consumerCts.Token))
                {
                    observed.Add(srq);
                    if (observed.Count >= 1)
                    {
                        consumerCts.Cancel();
                    }
                }
            }
            catch (OperationCanceledException) { }
        });

        // Give the gateway's forwarder ~300 ms to subscribe before
        // raising the SRQ.
        await Task.Delay(300, cts.Token);
        fake.RaiseServiceRequest(deviceName, statusByte: 0x42);

        try
        {
            await consumer.WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (TaskCanceledException) { }

        observed.Count.ShouldBeGreaterThanOrEqualTo(1);
        observed[0].Device.Value.ShouldBe("dut");

        await client.CloseAsync(device, cts.Token);
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
        FakeBackend Fake,
        DeviceName DeviceName
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
        var fake = new FakeBackend().ConfigureDevice(deviceName, "FAKE,VXI11,0,1.0");
        var gateway = new Vxi11GatewayServer(
            new FakeBackendFactory(fake),
            NullLogger<Vxi11GatewayServer>.Instance
        );
        return (gateway, srv, config, port, fake, deviceName);
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
