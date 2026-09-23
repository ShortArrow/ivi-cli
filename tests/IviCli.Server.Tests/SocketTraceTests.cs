using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace IviCli.Server.Tests;

/// <summary>
/// At Debug the SOCKET gateway says what it received and what it answered,
/// so a terminal run with <c>-v</c> shows the exchange without telemetry.
/// Long text is clipped: a response is the instrument's data, and a block
/// transfer would otherwise fill the log.
/// </summary>
public sealed class SocketTraceTests
{
    [Fact]
    public async Task A_query_and_its_response_are_logged_at_debug()
    {
        await using var harness = await SocketGatewayHarness.StartAsync();

        await ExchangeAsync(harness, "*IDN?", expectReply: true);

        var debug = DebugLines(harness);
        debug.ShouldContain(m => m.Contains("*IDN?"));
        debug.ShouldContain(m => m.Contains("FAKE,SOCKET,0,1.0"));
    }

    [Fact]
    public async Task A_write_is_logged_at_debug()
    {
        await using var harness = await SocketGatewayHarness.StartAsync();

        await ExchangeAsync(harness, "VOLT 1", expectReply: false);

        DebugLines(harness).ShouldContain(m => m.Contains("VOLT 1"));
    }

    [Fact]
    public async Task Long_text_is_clipped_and_says_how_much_was_left_out()
    {
        await using var harness = await SocketGatewayHarness.StartAsync();
        var longQuery = new string('A', 1000) + "?";

        await ExchangeAsync(harness, longQuery, expectReply: true);

        var debug = DebugLines(harness).Where(m => m.Contains("AAAA")).ToList();
        debug.ShouldNotBeEmpty();
        debug.ShouldAllBe(m => m.Length < 400);
        debug.ShouldAllBe(m => m.Contains("1001 chars"));
    }

    private static List<string> DebugLines(SocketGatewayHarness harness) =>
        harness
            .Logger.Entries.Where(e => e.Level == LogLevel.Debug)
            .Select(e => e.Message)
            .ToList();

    private static async Task ExchangeAsync(
        SocketGatewayHarness harness,
        string line,
        bool expectReply
    )
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, harness.ListenPort, harness.Token);
        var stream = tcp.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        await stream.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"), harness.Token);
        if (expectReply)
        {
            _ = await reader.ReadLineAsync(harness.Token);
        }
        else
        {
            await stream.WriteAsync("*OPC?\n"u8.ToArray(), harness.Token);
            _ = await reader.ReadLineAsync(harness.Token);
        }
    }
}
