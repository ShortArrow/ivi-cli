using System.Collections.Immutable;

namespace IviCli.Application.Backends;

/// <summary>
/// Options controlling a <c>visa scan</c> run (ADR 0008). The passive
/// discovery scanners (VXI-11 broadcast, LXI mDNS) ignore all of these; the
/// active <c>SocketSweepScanner</c> and the host-enrichment pass read them.
/// </summary>
/// <param name="SweepPorts">
/// TCP ports to sweep across the local subnet(s) to find raw-SOCKET
/// instruments that answer no broadcast/mDNS (e.g. Keithley 2701 on 1394),
/// and to additionally probe on every discovered host during enrichment.
/// Empty disables sweeping (the default).
/// </param>
/// <param name="Subnet">Explicit CIDR (e.g. <c>192.168.3.0/24</c>) overriding the auto local-subnet target.</param>
/// <param name="Host">Explicit single host overriding subnet enumeration; no sweep is performed.</param>
/// <param name="Verbose">
/// When set, open each discovered SOCKET endpoint and send <c>*IDN?</c> to
/// report the model, and surface diagnostics such as the resolved VXI-11 Core port.
/// </param>
public sealed record ScanOptions(
    ImmutableArray<int> SweepPorts,
    string? Subnet,
    string? Host,
    bool Verbose
)
{
    /// <summary>Passive scan with no sweep ports, no overrides, and no identification.</summary>
    public static ScanOptions Default { get; } =
        new(ImmutableArray<int>.Empty, null, null, Verbose: false);
}
