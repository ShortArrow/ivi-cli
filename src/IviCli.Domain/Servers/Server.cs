namespace IviCli.Domain.Servers;

/// <summary>Transport protocol exposed by a gateway server.</summary>
public enum ServerType
{
    /// <summary>The local in-process backend; no listener is started.</summary>
    Local,

    /// <summary>Raw TCP SOCKET, line-based SCPI (PRD §7.4).</summary>
    Socket,

    /// <summary>HiSLIP-compatible gateway (PRD §7.2).</summary>
    HiSlip,

    /// <summary>VXI-11-compatible gateway (PRD §7.3, future).</summary>
    Vxi11,
}

/// <summary>
/// A configured gateway server entity (PRD §6.3 / §7). Servers in
/// <see cref="ServerType.Local"/> mode have no listener; the other variants
/// open a <see cref="Bind"/>:<see cref="Port"/> socket when started.
/// </summary>
/// <param name="Name">The unique alias.</param>
/// <param name="Type">The protocol the server exposes.</param>
/// <param name="Bind">The bind address (loopback by default per ADR 0007 §4).</param>
/// <param name="Port">The TCP port the listener uses.</param>
public sealed record Server(ServerName Name, ServerType Type, IpAddress Bind, Port Port);
