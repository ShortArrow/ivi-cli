using System.Net;
using System.Net.Sockets;
using System.Text;
using IviCli.Backends.Local;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Backends.Local.Tests;

/// <summary>
/// A SOCKET device's terminators, through an installed VISA runtime to a
/// loopback listener standing in for the instrument (ADR 0055 §5).
/// </summary>
public sealed class LocalBackendVisaTerminationTests
{
    /// <summary>
    /// A line read would wait for an LF that never comes; with the read
    /// terminator set, each reply ends at its CR, even when two arrive in
    /// one packet.
    /// </summary>
    [Requires("ni-visa")]
    [Trait("Category", "Integration")]
    public async Task A_query_ends_with_the_write_terminator_and_reads_up_to_the_read_terminator()
    {
        var (request, replies) = await Exchange(
            Termination.Cr,
            Termination.CrLf,
            "1.5\r2.0\r",
            readsAfterQuery: 1
        );

        request.ShouldBe("MEAS?\r\n");
        replies.ShouldBe(["1.5", "2.0"]);
    }

    /// <summary>
    /// The reply ends at exactly the read terminator, so with LF a CR before
    /// it stays, where a line read would remove it.
    /// </summary>
    [Requires("ni-visa")]
    [Trait("Category", "Integration")]
    public async Task A_CR_before_an_LF_terminator_stays_in_the_reply()
    {
        var (_, replies) = await Exchange(Termination.Lf, Termination.Lf, "1.5\r\n");

        replies.ShouldBe(["1.5\r"]);
    }

    /// <summary>
    /// Sends <c>MEAS?</c> as a query to a SOCKET device with the given
    /// terminators whose instrument answers <paramref name="answer"/>, then
    /// reads <paramref name="readsAfterQuery"/> more replies.
    /// </summary>
    /// <returns>What the instrument received, and the replies in order.</returns>
    private static async Task<(string Request, List<string> Replies)> Exchange(
        Termination read,
        Termination write,
        string answer,
        int readsAfterQuery = 0
    )
    {
        var ct = TestContext.Current.CancellationToken;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var request = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var instrument = Answer(listener, "MEAS?" + write.Value, answer, request, ct);
        var device = new Device(
            DeviceName.From("dmm").ShouldBeOk(),
            VisaResource.Parse($"TCPIP0::127.0.0.1::{port}::SOCKET").ShouldBeOk(),
            Timeout.FromMilliseconds(2000).ShouldBeOk(),
            ReadTermination: read,
            WriteTermination: write
        );
        var backend = new LocalBackend(new VisaSessionFactory());

        (await backend.OpenAsync(device, ct)).ShouldBeOk();
        try
        {
            var replies = new List<string>
            {
                (
                    await backend.QueryAsync(device, ScpiQuery.From("MEAS?").ShouldBeOk(), ct)
                ).ShouldBeOk(),
            };
            for (var i = 0; i < readsAfterQuery; i++)
            {
                replies.Add((await backend.ReadAsync(device, ct)).ShouldBeOk());
            }
            return (await request.Task, replies);
        }
        finally
        {
            await backend.CloseAsync(device, ct);
            await instrument;
        }
    }

    /// <summary>
    /// Accepts one connection, reads until <paramref name="expected"/> has
    /// arrived, hands what it read to <paramref name="received"/>, answers
    /// <paramref name="reply"/>, and holds the connection until the client
    /// closes it, so no end of stream follows the reply for a read to stop at.
    /// </summary>
    private static async Task Answer(
        TcpListener listener,
        string expected,
        string reply,
        TaskCompletionSource<string> received,
        CancellationToken ct
    )
    {
        using var client = await listener.AcceptTcpClientAsync(ct);
        var stream = client.GetStream();
        var request = new StringBuilder();
        var buffer = new byte[256];
        while (request.Length < expected.Length)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0)
            {
                break;
            }
            request.Append(Encoding.Latin1.GetString(buffer, 0, read));
        }
        received.SetResult(request.ToString());
        await stream.WriteAsync(Encoding.Latin1.GetBytes(reply), ct);
        while (await stream.ReadAsync(buffer, ct) > 0) { }
    }
}
