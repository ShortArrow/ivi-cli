using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using IviCli.Backends.Fake;
using IviCli.Backends.HiSlip;
using IviCli.Backends.Vxi11;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.Server.HiSlip;
using IviCli.Server.Socket;
using IviCli.Server.Vxi11;
using IviCli.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace IviCli.Server.Tests;

/// <summary>
/// Gateway-side Activity emission (ADR 0040 / issue #17): every gateway
/// server emits one <c>gateway.session</c> span per connection and one
/// <c>gateway.message</c> span per handled operation, and the HiSLIP pair
/// can carry W3C trace context across the wire through the
/// vendor-specific VendorTraceContext message.
/// </summary>
public sealed class GatewayActivityTests
{
    /// <summary>
    /// Collects spans from the <c>IviCli.Gateway</c> source for one test.
    /// Tests filter by their unique server name, so parallel gateway
    /// tests sharing the process-wide source do not cross-talk.
    /// </summary>
    private sealed class GatewaySpanCollector : IDisposable
    {
        private readonly ActivityListener _listener;
        private readonly List<Activity> _stopped = new();

        public GatewaySpanCollector()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "IviCli.Gateway",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    lock (_stopped)
                    {
                        _stopped.Add(activity);
                    }
                },
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public List<Activity> StoppedFor(string serverName)
        {
            lock (_stopped)
            {
                return _stopped
                    .Where(a => (string?)a.GetTagItem("ivi.server") == serverName)
                    .ToList();
            }
        }

        public void Dispose() => _listener.Dispose();
    }

    private static readonly ActivitySource CallerSource = new("GatewayActivityTests.Caller");

    private static ActivityListener ListenTo(ActivitySource source)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = s => ReferenceEquals(s, source),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static (
        Device Device,
        IviCli.Domain.Servers.Server Server,
        ConfigDocument Config
    ) BuildTopology(string serverName, ServerType type, int port, string endpoint, string resource)
    {
        var deviceName = DeviceName.From("dut").ShouldBeOk();
        var device = new Device(
            deviceName,
            VisaResource.Parse(resource).ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );
        var name = ServerName.From(serverName).ShouldBeOk();
        var server = new IviCli.Domain.Servers.Server(
            name,
            type,
            IpAddress.From("127.0.0.1").ShouldBeOk(),
            Port.From(port).ShouldBeOk()
        );
        var route = new Route(name, PublicEndpoint.From(endpoint).ShouldBeOk(), deviceName);
        var config = ConfigDocument
            .Empty.AddDevice(device)
            .ShouldBeOk()
            .AddServer(server)
            .ShouldBeOk()
            .AddRoute(route)
            .ShouldBeOk();
        return (device, server, config);
    }

    [Fact]
    public async Task HiSlip_gateway_emits_session_and_message_spans()
    {
        using var spans = new GatewaySpanCollector();
        var port = GetFreePort();
        var (device, server, config) = BuildTopology(
            "hislip-span-srv",
            ServerType.HiSlip,
            port,
            "hislip0",
            "TCPIP0::127.0.0.1::hislip0::INSTR"
        );
        var fake = new FakeBackend()
            .ConfigureDevice(device.Name, "FAKE,HISLIP,0,1.0")
            .RespondToQuery(device.Name, "*IDN?", "FAKE,HISLIP,0,1.0");
        var gateway = new HiSlipGatewayServer(
            new FakeBackendFactory(fake),
            NullLogger<HiSlipGatewayServer>.Instance
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);

        var client = new HiSlipBackend(port);
        (await client.OpenAsync(device, cts.Token)).ShouldBeOk();
        (
            await client.QueryAsync(device, ScpiQuery.From("*IDN?").ShouldBeOk(), cts.Token)
        ).ShouldBeOk();
        (await client.TriggerAsync(device, cts.Token)).ShouldBeOk();
        // A second query serialises behind the Trigger, so the gateway has
        // handled all three operations once it answers.
        (
            await client.QueryAsync(device, ScpiQuery.From("*IDN?").ShouldBeOk(), cts.Token)
        ).ShouldBeOk();
        await client.CloseAsync(device, cts.Token);
        await StopAsync(cts, serverTask);

        var stopped = await WaitForSpansAsync(spans, "hislip-span-srv", minimum: 4);
        var session = stopped.ShouldContainSingleSpan("gateway.session");
        session.GetTagItem("ivi.transport").ShouldBe("hislip");
        session.GetTagItem("ivi.device").ShouldBe("dut");

        var messages = stopped.Where(a => a.OperationName == "gateway.message").ToList();
        messages.Count.ShouldBe(3);
        messages.ShouldAllBe(m => m.TraceId == session.TraceId);
        messages.ShouldAllBe(m => m.ParentSpanId == session.SpanId);
        messages.ShouldAllBe(m => (string?)m.GetTagItem("outcome") == "ok");
        messages.Count(m => (string?)m.GetTagItem("ivi.operation") == "scpi").ShouldBe(2);
        messages.Count(m => (string?)m.GetTagItem("ivi.operation") == "trigger").ShouldBe(1);
    }

    [Fact]
    public async Task HiSlip_client_propagates_trace_context_when_opted_in()
    {
        using var spans = new GatewaySpanCollector();
        using var callerListener = ListenTo(CallerSource);
        var port = GetFreePort();
        var (device, server, config) = BuildTopology(
            "hislip-propagate-srv",
            ServerType.HiSlip,
            port,
            "hislip0",
            "TCPIP0::127.0.0.1::hislip0::INSTR"
        );
        var fake = new FakeBackend()
            .ConfigureDevice(device.Name, "FAKE,HISLIP,0,1.0")
            .RespondToQuery(device.Name, "*IDN?", "FAKE,HISLIP,0,1.0");
        var gateway = new HiSlipGatewayServer(
            new FakeBackendFactory(fake),
            NullLogger<HiSlipGatewayServer>.Instance
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);

        var client = new HiSlipBackend(port) { PropagateTraceContext = true };
        (await client.OpenAsync(device, cts.Token)).ShouldBeOk();

        ActivityTraceId callerTraceId;
        ActivitySpanId callerSpanId;
        using (var caller = CallerSource.StartActivity("caller"))
        {
            caller.ShouldNotBeNull();
            callerTraceId = caller.TraceId;
            callerSpanId = caller.SpanId;
            (
                await client.QueryAsync(device, ScpiQuery.From("*IDN?").ShouldBeOk(), cts.Token)
            ).ShouldBeOk();
        }
        await client.CloseAsync(device, cts.Token);
        await StopAsync(cts, serverTask);

        var stopped = await WaitForSpansAsync(spans, "hislip-propagate-srv", minimum: 2);
        var session = stopped.ShouldContainSingleSpan("gateway.session");
        var message = stopped.ShouldContainSingleSpan("gateway.message");

        // The message span joins the caller's trace; the session stays a
        // local root and is attached as a link so both views connect.
        message.TraceId.ShouldBe(callerTraceId);
        message.ParentSpanId.ShouldBe(callerSpanId);
        session.TraceId.ShouldNotBe(callerTraceId);
        message.Links.ShouldContain(l => l.Context.SpanId == session.SpanId);
    }

    [Fact]
    public async Task HiSlip_client_does_not_propagate_by_default()
    {
        using var spans = new GatewaySpanCollector();
        using var callerListener = ListenTo(CallerSource);
        var port = GetFreePort();
        var (device, server, config) = BuildTopology(
            "hislip-nopropagate-srv",
            ServerType.HiSlip,
            port,
            "hislip0",
            "TCPIP0::127.0.0.1::hislip0::INSTR"
        );
        var fake = new FakeBackend()
            .ConfigureDevice(device.Name, "FAKE,HISLIP,0,1.0")
            .RespondToQuery(device.Name, "*IDN?", "FAKE,HISLIP,0,1.0");
        var gateway = new HiSlipGatewayServer(
            new FakeBackendFactory(fake),
            NullLogger<HiSlipGatewayServer>.Instance
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);

        var client = new HiSlipBackend(port);
        (await client.OpenAsync(device, cts.Token)).ShouldBeOk();
        ActivityTraceId callerTraceId;
        using (var caller = CallerSource.StartActivity("caller"))
        {
            caller.ShouldNotBeNull();
            callerTraceId = caller.TraceId;
            (
                await client.QueryAsync(device, ScpiQuery.From("*IDN?").ShouldBeOk(), cts.Token)
            ).ShouldBeOk();
        }
        await client.CloseAsync(device, cts.Token);
        await StopAsync(cts, serverTask);

        var stopped = await WaitForSpansAsync(spans, "hislip-nopropagate-srv", minimum: 2);
        var message = stopped.ShouldContainSingleSpan("gateway.message");
        message.TraceId.ShouldNotBe(callerTraceId);
    }

    [Fact]
    public async Task Vxi11_gateway_emits_session_and_message_spans()
    {
        using var spans = new GatewaySpanCollector();
        var port = GetFreePort();
        var (device, server, config) = BuildTopology(
            "vxi11-span-srv",
            ServerType.Vxi11,
            port,
            "inst0",
            "TCPIP0::127.0.0.1::inst0::INSTR"
        );
        var fake = new FakeBackend()
            .ConfigureDevice(device.Name, "FAKE,VXI11,0,1.0")
            .RespondToQuery(device.Name, "*IDN?", "FAKE,VXI11,0,1.0");
        var gateway = new Vxi11GatewayServer(
            new FakeBackendFactory(fake),
            NullLogger<Vxi11GatewayServer>.Instance
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);

        var client = new Vxi11Backend(port);
        (await client.OpenAsync(device, cts.Token)).ShouldBeOk();
        (
            await client.QueryAsync(device, ScpiQuery.From("*IDN?").ShouldBeOk(), cts.Token)
        ).ShouldBeOk();
        await client.CloseAsync(device, cts.Token);
        await StopAsync(cts, serverTask);

        var stopped = await WaitForSpansAsync(spans, "vxi11-span-srv", minimum: 2);
        var session = stopped.ShouldContainSingleSpan("gateway.session");
        session.GetTagItem("ivi.transport").ShouldBe("vxi11");
        var messages = stopped.Where(a => a.OperationName == "gateway.message").ToList();
        messages.ShouldNotBeEmpty();
        messages.ShouldAllBe(m => m.ParentSpanId == session.SpanId);
        messages.ShouldAllBe(m => (string?)m.GetTagItem("outcome") == "ok");
    }

    [Fact]
    public async Task Socket_gateway_emits_session_and_message_spans()
    {
        using var spans = new GatewaySpanCollector();
        var port = GetFreePort();
        var (device, server, config) = BuildTopology(
            "socket-span-srv",
            ServerType.Socket,
            port,
            port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "TCPIP0::127.0.0.1::5025::SOCKET"
        );
        var fake = new FakeBackend()
            .ConfigureDevice(device.Name, "FAKE,SOCKET,0,1.0")
            .RespondToQuery(device.Name, "*IDN?", "FAKE,SOCKET,0,1.0");
        var gateway = new SocketGatewayServer(
            new FakeBackendFactory(fake),
            NullLogger<SocketGatewayServer>.Instance
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = gateway.RunAsync(server, config, cts.Token);
        await WaitForListenerAsync(port, cts.Token);

        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, port, cts.Token);
            using var stream = tcp.GetStream();
            var request = System.Text.Encoding.ASCII.GetBytes("*IDN?\n");
            await stream.WriteAsync(request, cts.Token);
            var buffer = new byte[256];
            var read = await stream.ReadAsync(buffer, cts.Token);
            System
                .Text.Encoding.ASCII.GetString(buffer, 0, read)
                .TrimEnd('\n')
                .ShouldBe("FAKE,SOCKET,0,1.0");
        }
        await StopAsync(cts, serverTask);

        var stopped = await WaitForSpansAsync(spans, "socket-span-srv", minimum: 2);
        var session = stopped.ShouldContainSingleSpan("gateway.session");
        session.GetTagItem("ivi.transport").ShouldBe("socket");
        var message = stopped.ShouldContainSingleSpan("gateway.message");
        message.ParentSpanId.ShouldBe(session.SpanId);
        message.GetTagItem("outcome").ShouldBe("ok");
    }

    private static async Task<IReadOnlyList<Activity>> WaitForSpansAsync(
        GatewaySpanCollector spans,
        string serverName,
        int minimum
    )
    {
        // The session span stops when the gateway's connection handler
        // unwinds, which races the client-side close; poll briefly.
        for (var i = 0; i < 50; i++)
        {
            var stopped = spans.StoppedFor(serverName);
            if (stopped.Count >= minimum)
            {
                return stopped;
            }
            await Task.Delay(50);
        }
        return spans.StoppedFor(serverName);
    }

    private static async Task StopAsync(CancellationTokenSource cts, Task serverTask)
    {
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
        throw new TimeoutException($"gateway did not bind to port {port}");
    }
}

/// <summary>Span-list assertion helpers for <see cref="GatewayActivityTests"/>.</summary>
internal static class GatewayActivityAssertions
{
    public static Activity ShouldContainSingleSpan(
        this IReadOnlyList<Activity> activities,
        string operationName
    )
    {
        var matching = activities.Where(a => a.OperationName == operationName).ToList();
        matching.Count.ShouldBe(
            1,
            $"expected exactly one '{operationName}' span, got {matching.Count} "
                + $"of [{string.Join(", ", activities.Select(a => a.OperationName))}]"
        );
        return matching[0];
    }
}
