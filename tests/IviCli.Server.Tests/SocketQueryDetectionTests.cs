using System.Net;
using System.Net.Sockets;
using System.Text;
using Shouldly;

namespace IviCli.Server.Tests;

/// <summary>
/// The SOCKET gateway answers every request whose header ends in <c>?</c>,
/// including one with parameters or trailing whitespace; sending such a
/// request to the backend as a write left the client waiting for its timeout.
/// </summary>
public sealed class SocketQueryDetectionTests
{
    [Theory]
    [InlineData("MEAS:VOLT? CH1", "MEAS:VOLT? CH1")]
    [InlineData("MEAS:VOLT? (@1)", "MEAS:VOLT? (@1)")]
    [InlineData("*IDN? ", "FAKE,SOCKET,0,1.0")]
    [InlineData("*IDN?\t", "FAKE,SOCKET,0,1.0")]
    public async Task A_query_gets_its_reply(string request, string expected)
    {
        await using var harness = await SocketGatewayHarness.StartAsync();
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, harness.ListenPort, harness.Token);
        var stream = tcp.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

        await stream.WriteAsync(Encoding.UTF8.GetBytes(request + "\n"), harness.Token);
        using var replyTimeout = CancellationTokenSource.CreateLinkedTokenSource(harness.Token);
        replyTimeout.CancelAfter(TimeSpan.FromSeconds(2));

        (await reader.ReadLineAsync(replyTimeout.Token)).ShouldBe(expected);
    }
}
