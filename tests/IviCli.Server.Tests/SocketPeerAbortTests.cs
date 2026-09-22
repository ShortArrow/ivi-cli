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
/// A client that resets its connection (killed process, abortive close,
/// timeout-and-reconnect) ends the session like any other disconnect: the
/// gateway says so in one line and never reports it as an unexpected error.
/// </summary>
public sealed class SocketPeerAbortTests
{
    [Fact]
    public async Task Client_reset_while_idle_is_logged_as_a_disconnect_not_an_error()
    {
        await using var harness = await SocketGatewayHarness.StartAsync();

        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, harness.ListenPort, harness.Token);
            await harness.WaitForSecondAsync("client connected");
            tcp.Client.Close(0);
        }

        await harness.WaitForSecondAsync("client disconnected");
        harness.Logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Error);
        harness.Logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Information && entry.Message.Contains("aborted")
        );
    }

    [Fact]
    public async Task Client_reset_after_a_query_is_logged_as_a_disconnect_not_an_error()
    {
        await using var harness = await SocketGatewayHarness.StartAsync();

        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, harness.ListenPort, harness.Token);
            var stream = tcp.GetStream();
            await stream.WriteAsync(Encoding.UTF8.GetBytes("*IDN?\n"), harness.Token);
            var buffer = new byte[256];
            _ = await stream.ReadAsync(buffer, harness.Token);
            tcp.Client.Close(0);
        }

        await harness.WaitForSecondAsync("client disconnected");
        harness.Logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Client_leaving_with_responses_unread_is_logged_as_a_disconnect_not_an_error()
    {
        await using var harness = await SocketGatewayHarness.StartAsync();

        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, harness.ListenPort, harness.Token);
            var burst = string.Concat(Enumerable.Repeat("*IDN?\n", 20000));
            await tcp.GetStream().WriteAsync(Encoding.UTF8.GetBytes(burst), harness.Token);
        }

        await harness.WaitForSecondAsync("client disconnected");
        harness.Logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Error);
    }
}
