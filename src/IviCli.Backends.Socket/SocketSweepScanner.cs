using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Visa;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IviCli.Backends.Socket;

/// <summary>
/// Active TCP-sweep scanner for raw-SOCKET instruments that answer no
/// broadcast or mDNS discovery (ADR 0008) — e.g. a Keithley 2701 on its
/// vendor port 1394. Opt-in: only runs when <see cref="ScanOptions.SweepPorts"/>
/// is non-empty. For each swept port it opens a bounded-timeout TCP connection
/// to every target address and reports a <c>TCPIP::host::port::SOCKET</c>
/// resource for each host that accepts the connection.
///
/// Targets default to every operational IPv4 subnet no larger than a <c>/24</c>
/// (APIPA and oversized subnets are skipped for safety); <c>--subnet</c> or
/// <c>--host</c> override the target set.
/// </summary>
public sealed class SocketSweepScanner : IBackendScanner
{
    private const int MinAutoPrefixLength = 24;
    private const int MaxConcurrency = 128;

    private readonly IEndpointProber _prober;
    private readonly ILogger<SocketSweepScanner> _logger;

    /// <summary>Creates a sweep scanner bound to the supplied endpoint prober.</summary>
    public SocketSweepScanner(IEndpointProber prober, ILogger<SocketSweepScanner>? logger = null)
    {
        _prober = prober;
        _logger = logger ?? NullLogger<SocketSweepScanner>.Instance;
    }

    /// <inheritdoc/>
    public async Task<Result<ImmutableArray<DiscoveredResource>, BackendError>> ScanAsync(
        ScanOptions options,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        if (options.SweepPorts.IsDefaultOrEmpty)
        {
            return Empty();
        }

        var hosts = ResolveTargets(options);
        if (hosts.Count == 0)
        {
            return Empty();
        }

        var ports = options.SweepPorts.Distinct().ToArray();
        _logger.LogDebug(
            "Socket sweep: {HostCount} hosts × {PortCount} ports",
            hosts.Count,
            ports.Length
        );

        var found = new ConcurrentBag<DiscoveredResource>();
        using var gate = new SemaphoreSlim(MaxConcurrency);
        var probes =
            from host in hosts
            from port in ports
            select ProbeOneAsync(host, port, options.Verbose, gate, found, ct);
        await Task.WhenAll(probes).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();
        return Result.Success<ImmutableArray<DiscoveredResource>, BackendError>(
            found.ToImmutableArray()
        );
    }

    private async Task ProbeOneAsync(
        string host,
        int port,
        bool verbose,
        SemaphoreSlim gate,
        ConcurrentBag<DiscoveredResource> found,
        CancellationToken ct
    )
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var probe = await _prober.ProbeAsync(host, port, verbose, ct).ConfigureAwait(false);
            if (!probe.Open)
            {
                return;
            }
            var parsed = VisaResource.Parse($"TCPIP0::{host}::{port}::SOCKET");
            if (parsed is Result<VisaResource, VisaResourceError>.Ok { Value: var resource })
            {
                found.Add(new DiscoveredResource(resource, probe.Idn));
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private IReadOnlyList<string> ResolveTargets(ScanOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Host))
        {
            return [options.Host];
        }
        if (!string.IsNullOrWhiteSpace(options.Subnet))
        {
            var cidr = SocketSweepTargets.TryParseCidr(options.Subnet);
            if (cidr is null)
            {
                _logger.LogWarning("Ignoring malformed --subnet '{Subnet}'", options.Subnet);
                return [];
            }
            return SocketSweepTargets
                .SubnetHosts(cidr.Value.Network, cidr.Value.PrefixLength)
                .Select(ip => ip.ToString())
                .ToArray();
        }
        return EnumerateAutoTargets();
    }

    private static IReadOnlyList<string> EnumerateAutoTargets()
    {
        var hosts = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }
                IPAddress? mask = null;
                try
                {
                    mask = addr.IPv4Mask;
                }
                catch (Exception)
                {
                    mask = null;
                }
                if (
                    !SocketSweepTargets.ShouldSweep(
                        nic.OperationalStatus,
                        nic.NetworkInterfaceType,
                        addr.Address.AddressFamily,
                        addr.Address,
                        mask,
                        MinAutoPrefixLength
                    )
                )
                {
                    continue;
                }
                foreach (
                    var ip in SocketSweepTargets.SubnetHosts(
                        addr.Address,
                        SocketSweepTargets.PrefixLength(mask!)
                    )
                )
                {
                    var text = ip.ToString();
                    if (seen.Add(text))
                    {
                        hosts.Add(text);
                    }
                }
            }
        }
        return hosts;
    }

    private static Result<ImmutableArray<DiscoveredResource>, BackendError> Empty() =>
        Result.Success<ImmutableArray<DiscoveredResource>, BackendError>(
            ImmutableArray<DiscoveredResource>.Empty
        );
}
