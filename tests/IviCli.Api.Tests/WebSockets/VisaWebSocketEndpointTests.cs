using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using IviCli.Api.WebSockets;
using IviCli.Application.Backends;
using IviCli.Backends.Fake;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Api.Tests.WebSockets;

public sealed class VisaWebSocketEndpointTests
{
    private static Device Dev(string name) =>
        new(
            DeviceName.From(name).ShouldBeOk(),
            VisaResource.Parse("TCPIP0::192.168.0.10::inst0::INSTR").ShouldBeOk(),
            Timeout.FromMilliseconds(3000).ShouldBeOk()
        );

    private static async Task SendAsync(WebSocket socket, string text, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    private static async Task<JsonDocument> ReceiveAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var result = await socket.ReceiveAsync(buffer, ct);
        return JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count));
    }

    [Fact]
    public async Task Query_round_trip_emits_Response_event_with_latency()
    {
        var doc = ConfigDocument.Empty.AddDevice(Dev("psu1")).ShouldBeOk();
        var backend = new FakeBackend();
        backend.RespondToQuery(DeviceName.From("psu1").ShouldBeOk(), "*IDN?", "ACME,PSU,1,1.0");
        await using var host = await ApiTestHost.StartAsync(doc, backend);
        var ws = host.Server.CreateWebSocketClient();
        using var socket = await ws.ConnectAsync(
            new Uri("ws://localhost/v1/devices/psu1/visa"),
            TestContext.Current.CancellationToken
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await SendAsync(socket, "{\"op\":\"query\",\"scpi\":\"*IDN?\"}", cts.Token);
        using var reply = await ReceiveAsync(socket, cts.Token);

        reply.RootElement.GetProperty("event").GetString().ShouldBe("response");
        reply.RootElement.GetProperty("scpi").GetString().ShouldBe("*IDN?");
        reply.RootElement.GetProperty("response").GetString().ShouldBe("ACME,PSU,1,1.0");
        reply.RootElement.GetProperty("latencyMs").GetInt32().ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Write_emits_Ack_event()
    {
        var doc = ConfigDocument.Empty.AddDevice(Dev("psu1")).ShouldBeOk();
        await using var host = await ApiTestHost.StartAsync(doc);
        var ws = host.Server.CreateWebSocketClient();
        using var socket = await ws.ConnectAsync(
            new Uri("ws://localhost/v1/devices/psu1/visa"),
            TestContext.Current.CancellationToken
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await SendAsync(socket, "{\"op\":\"write\",\"scpi\":\"OUTP ON\"}", cts.Token);
        using var reply = await ReceiveAsync(socket, cts.Token);

        reply.RootElement.GetProperty("event").GetString().ShouldBe("ack");
        reply.RootElement.GetProperty("scpi").GetString().ShouldBe("OUTP ON");
    }

    [Theory]
    [InlineData("{\"op\":\"query\",\"scpi\":\"*IDN?\"}")]
    [InlineData("{\"op\":\"write\",\"scpi\":\"OUTP ON\"}")]
    public async Task A_backend_failure_emits_the_error_rendered(string frame)
    {
        var doc = ConfigDocument.Empty.AddDevice(Dev("psu1")).ShouldBeOk();
        var backend = new FakeBackend();
        backend.FailNextOpen(
            DeviceName.From("psu1").ShouldBeOk(),
            new TransportDisconnected("peer closed")
        );
        await using var host = await ApiTestHost.StartAsync(doc, backend);
        var ws = host.Server.CreateWebSocketClient();
        using var socket = await ws.ConnectAsync(
            new Uri("ws://localhost/v1/devices/psu1/visa"),
            TestContext.Current.CancellationToken
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await SendAsync(socket, frame, cts.Token);
        using var reply = await ReceiveAsync(socket, cts.Token);

        reply.RootElement.GetProperty("code").GetString().ShouldBe("backend_failure");
        reply
            .RootElement.GetProperty("message")
            .GetString()
            .ShouldBe("transport disconnected: peer closed");
    }

    [Theory]
    [InlineData("{\"op\":\"query\",\"scpi\":\"*IDN?\"}")]
    [InlineData("{\"op\":\"write\",\"scpi\":\"OUTP ON\"}")]
    public async Task A_config_store_failure_leaves_the_stores_reason_out(string frame)
    {
        var store = new FakeConfigStore(ConfigDocument.Empty.AddDevice(Dev("psu1")).ShouldBeOk());
        await using var host = await ApiTestHost.StartAsync(
            ConfigDocument.Empty,
            configStore: store
        );
        var ws = host.Server.CreateWebSocketClient();
        using var socket = await ws.ConnectAsync(
            new Uri("ws://localhost/v1/devices/psu1/visa"),
            TestContext.Current.CancellationToken
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        store.FailNextLoadWith("/home/someone/.config/ivicli/config.toml is gone");

        await SendAsync(socket, frame, cts.Token);
        using var reply = await ReceiveAsync(socket, cts.Token);

        reply.RootElement.GetProperty("code").GetString().ShouldBe("config_store_failure");
        reply
            .RootElement.GetProperty("message")
            .GetString()
            .ShouldBe("the configuration or session store could not be used");
    }

    [Fact]
    public async Task Malformed_frame_emits_protocol_error_event_and_keeps_socket_open()
    {
        var doc = ConfigDocument.Empty.AddDevice(Dev("psu1")).ShouldBeOk();
        await using var host = await ApiTestHost.StartAsync(doc);
        var ws = host.Server.CreateWebSocketClient();
        using var socket = await ws.ConnectAsync(
            new Uri("ws://localhost/v1/devices/psu1/visa"),
            TestContext.Current.CancellationToken
        );
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await SendAsync(socket, "this is not json", cts.Token);
        using var reply = await ReceiveAsync(socket, cts.Token);

        reply.RootElement.GetProperty("event").GetString().ShouldBe("error");
        reply.RootElement.GetProperty("code").GetString().ShouldBe("protocol_error");
        socket.State.ShouldBe(WebSocketState.Open);
    }

    [Fact]
    public async Task Unknown_device_emits_error_event_and_closes()
    {
        await using var host = await ApiTestHost.StartAsync(ConfigDocument.Empty);
        var ws = host.Server.CreateWebSocketClient();

        using var socket = await ws.ConnectAsync(
            new Uri("ws://localhost/v1/devices/nope/visa"),
            TestContext.Current.CancellationToken
        );

        using var reply = await ReceiveAsync(socket, TestContext.Current.CancellationToken);
        reply.RootElement.GetProperty("event").GetString().ShouldBe("error");
        reply.RootElement.GetProperty("code").GetString().ShouldBe("device_not_found");

        // Drain the close frame.
        var buffer = new byte[64];
        var closing = await socket.ReceiveAsync(buffer, TestContext.Current.CancellationToken);
        closing.MessageType.ShouldBe(WebSocketMessageType.Close);
    }
}
