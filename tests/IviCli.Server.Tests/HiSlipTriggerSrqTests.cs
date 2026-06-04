using System.Net;
using System.Net.Sockets;
using IviCli.Application.Backends;
using IviCli.Backends.Fake;
using IviCli.Backends.HiSlip;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.Server.HiSlip;
using IviCli.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace IviCli.Server.Tests;

/// <summary>
/// End-to-end Trigger + ServiceRequest tests across the
/// HiSlipBackend → HiSlipGatewayServer → FakeBackend stack (ADR 0041).
/// </summary>
public sealed class HiSlipTriggerSrqTests
{
    [Fact]
    public async Task Backend_TriggerAsync_lands_at_FakeBackend_via_gateway()
    {
        var (gateway, server, config, port, fake, deviceName) = BuildHarness();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);

        var device = config.FindDevice(deviceName)!;
        var client = new HiSlipBackend(port);
        (await client.OpenAsync(device, cts.Token)).ShouldBeOk();

        (await client.TriggerAsync(device, cts.Token)).ShouldBeOk();
        await WaitForAsync(() => fake.TriggerCountFor(deviceName) >= 1);
        fake.TriggerCountFor(deviceName).ShouldBe(1);

        await client.CloseAsync(device, cts.Token);
        await cts.CancelAsync();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task FakeBackend_RaiseServiceRequest_propagates_to_HiSlipBackend_stream()
    {
        var (gateway, server, config, port, fake, deviceName) = BuildHarness();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);

        var device = config.FindDevice(deviceName)!;
        var client = new HiSlipBackend(port);
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

        // Give the gateway's forwarder ~200 ms to pick up the binding
        // before raising the SRQ on FakeBackend.
        await Task.Delay(300, cts.Token);
        fake.RaiseServiceRequest(deviceName, statusByte: 0x41);

        try
        {
            await consumer.WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (TaskCanceledException) { }

        observed.Count.ShouldBeGreaterThanOrEqualTo(1);
        observed[0].StatusByte.ShouldBe<byte>(0x41);

        await client.CloseAsync(device, cts.Token);
        await cts.CancelAsync();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException) { }
    }

    private static (
        HiSlipGatewayServer Gateway,
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
            VisaResource.Parse("TCPIP0::127.0.0.1::hislip0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );
        var serverName = ServerName.From("hislip-srv").ShouldBeOk();
        var endpoint = PublicEndpoint.From("hislip0").ShouldBeOk();
        var bind = IpAddress.From("127.0.0.1").ShouldBeOk();
        var portValue = Port.From(port).ShouldBeOk();
        var srv = new IviCli.Domain.Servers.Server(serverName, ServerType.HiSlip, bind, portValue);
        var route = new Route(serverName, endpoint, deviceName);
        var config = ConfigDocument
            .Empty.AddDevice(device)
            .ShouldBeOk()
            .AddServer(srv)
            .ShouldBeOk()
            .AddRoute(route)
            .ShouldBeOk();
        var fake = new FakeBackend().ConfigureDevice(deviceName, "FAKE,HISLIP,0,1.0");
        var gateway = new HiSlipGatewayServer(
            new FakeBackendFactory(fake),
            NullLogger<HiSlipGatewayServer>.Instance
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
        throw new TimeoutException($"HiSLIP gateway did not bind to port {port}");
    }

    private static async Task WaitForAsync(Func<bool> predicate, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }
            await Task.Delay(20);
        }
    }
}
