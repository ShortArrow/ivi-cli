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
        var ct = TestContext.Current.CancellationToken;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var request = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var instrument = Answer(listener, "MEAS?\r\n", "1.5\r2.0\r", request, ct);
        var device = new Device(
            DeviceName.From("dmm").ShouldBeOk(),
            VisaResource.Parse($"TCPIP0::127.0.0.1::{port}::SOCKET").ShouldBeOk(),
            Timeout.FromMilliseconds(2000).ShouldBeOk(),
            ReadTermination: Termination.Cr,
            WriteTermination: Termination.CrLf
        );
        var backend = new LocalBackend(new VisaSessionFactory());

        (await backend.OpenAsync(device, ct)).ShouldBeOk();
        try
        {
            var reply = await backend.QueryAsync(device, ScpiQuery.From("MEAS?").ShouldBeOk(), ct);

            reply.ShouldBeOk().ShouldBe("1.5");
            (await request.Task).ShouldBe("MEAS?\r\n");
            (await backend.ReadAsync(device, ct)).ShouldBeOk().ShouldBe("2.0");
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
