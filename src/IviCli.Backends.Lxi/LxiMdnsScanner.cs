using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Visa;
using Makaretu.Dns;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IviCli.Backends.Lxi;

/// <summary>
/// LXI-compatible mDNS / DNS-SD scanner (ADR 0008, Batch W).
///
/// Queries the four service types defined by LXI 1.4+:
/// <list type="bullet">
/// <item><c>_hislip._tcp.local</c> — HiSlip-capable instruments.</item>
/// <item><c>_vxi-11._tcp.local</c> — VXI-11-capable instruments.</item>
/// <item><c>_scpi-raw._tcp.local</c> — raw-socket SCPI listeners.</item>
/// <item><c>_lxi._tcp.local</c> — generic LXI marker.</item>
/// </list>
///
/// Each <see cref="ServiceDiscovery.ServiceInstanceDiscovered"/>
/// event delivers a DNS message whose Additional records carry the
/// SRV + A / AAAA tuples we need, so a single round-trip per
/// instance suffices.
/// </summary>
public sealed class LxiMdnsScanner : IBackendScanner
{
    private static readonly string[] ServiceTypes =
    {
        "_hislip._tcp.local",
        "_vxi-11._tcp.local",
        "_scpi-raw._tcp.local",
        "_lxi._tcp.local",
    };

    private readonly TimeSpan _discoveryWindow;
    private readonly ILogger<LxiMdnsScanner> _logger;

    /// <summary>Creates a scanner that listens for responses for the supplied window (default 3 s).</summary>
    public LxiMdnsScanner(TimeSpan? discoveryWindow = null, ILogger<LxiMdnsScanner>? logger = null)
    {
        _discoveryWindow = discoveryWindow ?? TimeSpan.FromSeconds(3);
        _logger = logger ?? NullLogger<LxiMdnsScanner>.Instance;
    }

    /// <inheritdoc/>
    public async Task<Result<ImmutableArray<DiscoveredResource>, BackendError>> ScanAsync(
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();

        // Concurrent map: instance domain name → (service type, host, port).
        // The same physical device may announce on multiple service types;
        // the dictionary key keeps each (service-type-specific) entry
        // separately so the VISA-resource string preserves protocol intent.
        var collected = new ConcurrentDictionary<string, DiscoveryHit>();

        using var mdns = new MulticastService();
        using var serviceDiscovery = new ServiceDiscovery(mdns);

        serviceDiscovery.ServiceInstanceDiscovered += (_, e) =>
        {
            try
            {
                CollectFromInstance(e, collected);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "mDNS instance handler failed for {Name}",
                    e.ServiceInstanceName
                );
            }
        };

        try
        {
            mdns.Start();
            foreach (var st in ServiceTypes)
            {
                serviceDiscovery.QueryServiceInstances(new DomainName(st));
            }

            try
            {
                await Task.Delay(_discoveryWindow, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Window elapsed — expected exit path.
            }
        }
        finally
        {
            mdns.Stop();
        }

        ct.ThrowIfCancellationRequested();

        var resources = collected
            .Values.Select(BuildDiscovered)
            .Where(r => r is not null)
            .Select(r => r!)
            .ToImmutableArray();

        return Result.Success<ImmutableArray<DiscoveredResource>, BackendError>(resources);
    }

    private static string? ResolveServiceType(string instanceName)
    {
        foreach (var st in ServiceTypes)
        {
            if (instanceName.EndsWith(st, StringComparison.OrdinalIgnoreCase))
            {
                return st;
            }
        }
        return null;
    }

    private static void CollectFromInstance(
        ServiceInstanceDiscoveryEventArgs e,
        ConcurrentDictionary<string, DiscoveryHit> collected
    )
    {
        var instanceName = e.ServiceInstanceName.ToString();
        var serviceType = ResolveServiceType(instanceName);
        if (serviceType is null)
        {
            return;
        }

        // Index A / AAAA records by name first so SRV lookup can resolve
        // the host immediately. Prefer IPv4 (single address slot, AAAA
        // only fills when no A is present).
        var hosts = new Dictionary<DomainName, IPAddress>();
        foreach (var record in e.Message.Answers.Concat(e.Message.AdditionalRecords))
        {
            switch (record)
            {
                case ARecord a:
                    hosts[a.Name] = a.Address;
                    break;
                case AAAARecord aaaa when !hosts.ContainsKey(aaaa.Name):
                    hosts[aaaa.Name] = aaaa.Address;
                    break;
            }
        }

        foreach (var record in e.Message.Answers.Concat(e.Message.AdditionalRecords))
        {
            if (record is not SRVRecord srv)
            {
                continue;
            }
            // Only accept SRV records that belong to the discovered
            // instance — defensive against unrelated piggybacked answers.
            if (
                !string.Equals(
                    srv.Name.ToString(),
                    instanceName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                continue;
            }
            var hostString = hosts.TryGetValue(srv.Target, out var ip)
                ? ip.ToString()
                : srv.Target.ToString().TrimEnd('.');

            collected[instanceName] = new DiscoveryHit(serviceType, hostString, srv.Port);
        }
    }

    private static DiscoveredResource? BuildDiscovered(DiscoveryHit hit)
    {
        // Translate the service type into the matching VISA resource shape.
        // _lxi alone has no protocol shape; we surface the matching
        // protocol-specific announcements when the device also publishes
        // _hislip / _vxi-11 / _scpi-raw.
        var raw = hit.ServiceType switch
        {
            "_hislip._tcp.local" => $"TCPIP0::{hit.Host}::hislip0::INSTR",
            "_vxi-11._tcp.local" => $"TCPIP0::{hit.Host}::inst0::INSTR",
            "_scpi-raw._tcp.local" => $"TCPIP0::{hit.Host}::{hit.Port}::SOCKET",
            _ => null,
        };
        if (raw is null)
        {
            return null;
        }

        var parsed = VisaResource.Parse(raw);
        if (parsed is not Result<VisaResource, VisaResourceError>.Ok { Value: var resource })
        {
            return null;
        }
        return new DiscoveredResource(resource, Idn: null);
    }

    private sealed record DiscoveryHit(string ServiceType, string Host, int Port);
}
