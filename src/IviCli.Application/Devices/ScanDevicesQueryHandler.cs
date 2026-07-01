using System.Collections.Immutable;
using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Visa;

namespace IviCli.Application.Devices;

/// <summary>
/// Application-layer handler for <c>visa scan</c>. Runs every registered
/// <see cref="IBackendScanner"/> (VXI-11 broadcast, LXI mDNS, socket sweep),
/// then <em>enriches</em> each discovered host by probing the well-known
/// instrument ports it did not already surface — so a device found via VXI-11
/// also reports its HiSLIP and SCPI-RAW access paths (ADR 0008). The first
/// transport failure is surfaced only when nothing at all was discovered, so
/// one misbehaving backend never blanks an otherwise useful result.
/// </summary>
public sealed class ScanDevicesQueryHandler
{
    /// <summary>Well-known ports probed on every discovered host during enrichment.</summary>
    private const int HiSlipPort = 4880;
    private const int ScpiRawPort = 5025;

    private readonly IEnumerable<IBackendScanner> _scanners;
    private readonly IEndpointProber _prober;

    /// <summary>Creates a handler bound to the supplied scanners and endpoint prober.</summary>
    public ScanDevicesQueryHandler(IEnumerable<IBackendScanner> scanners, IEndpointProber prober)
    {
        _scanners = scanners;
        _prober = prober;
    }

    /// <summary>Performs the aggregated scan.</summary>
    public async Task<Result<ScanResult, ScanDevicesError>> HandleAsync(
        ScanDevicesQuery query,
        CancellationToken ct
    )
    {
        var options = query.Options;
        var collected = ImmutableArray.CreateBuilder<DiscoveredResource>();
        ScanDevicesError? firstError = null;

        foreach (var scanner in _scanners)
        {
            ct.ThrowIfCancellationRequested();
            var scanResult = await scanner.ScanAsync(options, ct);
            switch (scanResult)
            {
                case Result<ImmutableArray<DiscoveredResource>, BackendError>.Ok ok:
                    collected.AddRange(ok.Value);
                    break;
                case Result<ImmutableArray<DiscoveredResource>, BackendError>.Error err:
                    firstError ??= new ScanDevicesScannerFailure(err.Err);
                    break;
                default:
                    throw new InvalidOperationException("Unknown Result variant");
            }
        }

        if (firstError is not null && collected.Count == 0)
        {
            return Result.Failure<ScanResult, ScanDevicesError>(firstError);
        }

        var enriched = await EnrichAsync(collected.ToImmutable(), options, ct);
        return Result.Success<ScanResult, ScanDevicesError>(new ScanResult(enriched));
    }

    /// <summary>
    /// For every host already discovered, probes the well-known instrument
    /// ports it has not yet surfaced (HiSLIP 4880, SCPI-RAW 5025, and any
    /// swept ports) and appends a resource per reachable protocol, deduped by
    /// canonical resource string.
    /// </summary>
    private async Task<ImmutableArray<DiscoveredResource>> EnrichAsync(
        ImmutableArray<DiscoveredResource> discovered,
        ScanOptions options,
        CancellationToken ct
    )
    {
        var byCanonical = new Dictionary<string, DiscoveredResource>(StringComparer.Ordinal);
        foreach (var r in discovered)
        {
            byCanonical.TryAdd(r.Resource.ToCanonical(), r);
        }

        var socketPorts = new[] { ScpiRawPort }
            .Concat(options.SweepPorts.IsDefaultOrEmpty ? [] : options.SweepPorts)
            .Distinct()
            .ToArray();

        foreach (var host in HostsOf(discovered))
        {
            ct.ThrowIfCancellationRequested();

            // HiSLIP: an open 4880 means the device speaks HiSLIP; it is not a
            // raw-SCPI port, so never send *IDN? here (that needs the handshake).
            await AddIfOpenAsync(
                host,
                HiSlipPort,
                $"TCPIP0::{host}::hislip0::INSTR",
                false,
                byCanonical,
                ct
            );

            foreach (var port in socketPorts)
            {
                await AddIfOpenAsync(
                    host,
                    port,
                    $"TCPIP0::{host}::{port}::SOCKET",
                    options.Verbose,
                    byCanonical,
                    ct
                );
            }
        }

        return byCanonical.Values.ToImmutableArray();
    }

    private async Task AddIfOpenAsync(
        string host,
        int port,
        string rawResource,
        bool identify,
        Dictionary<string, DiscoveredResource> byCanonical,
        CancellationToken ct
    )
    {
        if (VisaResource.Parse(rawResource) is not Result<VisaResource, VisaResourceError>.Ok ok)
        {
            return;
        }
        var canonical = ok.Value.ToCanonical();
        if (byCanonical.ContainsKey(canonical))
        {
            return;
        }
        var probe = await _prober.ProbeAsync(host, port, identify, ct);
        if (probe.Open)
        {
            byCanonical[canonical] = new DiscoveredResource(ok.Value, probe.Idn);
        }
    }

    private static IEnumerable<string> HostsOf(ImmutableArray<DiscoveredResource> discovered) =>
        discovered
            .Select(r =>
                r.Resource switch
                {
                    VisaResource.Tcpip t => t.Host,
                    VisaResource.TcpipSocket s => s.Host,
                    _ => null,
                }
            )
            .Where(h => h is not null)
            .Select(h => h!)
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
