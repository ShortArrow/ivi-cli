using System.Net;
using System.Net.Sockets;
using IviCli.Domain.Servers;

namespace IviCli.TestKit;

/// <summary>
/// Starts a gateway on a loopback port that no other process holds.
/// </summary>
/// <remarks>
/// A port found free is free only until someone binds it. Test assemblies
/// run in parallel processes, so another test can take the port between
/// the probe and the gateway's bind; the gateway then fails to bind while
/// the test's client reaches the other process's server. Every gateway
/// binds before its first <c>await</c>, so a run task that is already
/// complete when <c>RunAsync</c> returns is a lost bind, and the gateway
/// is started again on a fresh port.
/// </remarks>
public static class LoopbackGateway
{
    private const int MaxAttempts = 10;

    /// <summary>
    /// Starts <paramref name="run"/> for <paramref name="server"/>, and again
    /// for a copy on a fresh loopback port while the bind is lost.
    /// </summary>
    /// <returns>
    /// The port the gateway listens on and its run task. After
    /// <see cref="MaxAttempts"/> lost binds, the last completed task, whose
    /// result carries the bind failure.
    /// </returns>
    public static (int Port, Task<T> Run) Start<T>(Server server, Func<Server, Task<T>> run)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(run);
        var candidate = server;
        var task = run(candidate);
        for (var attempt = 1; task.IsCompleted && attempt < MaxAttempts; attempt++)
        {
            candidate = server with { Port = Port.From(FreePort()).ShouldBeOk() };
            task = run(candidate);
        }
        return (candidate.Port.Value, task);
    }

    /// <summary>A loopback TCP port that was free a moment ago.</summary>
    public static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
