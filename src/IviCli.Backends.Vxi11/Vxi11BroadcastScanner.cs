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
using static IviCli.Domain.Protocols.Vxi11Constants;

namespace IviCli.Backends.Vxi11;

/// <summary>
/// VXI-11 portmapper broadcast scanner (ADR 0008).
///
/// Enumerates every operational IPv4 interface and sends an ONC RPC
/// <c>PMAPPROC_GETPORT</c> request asking for the VXI-11 Device Core
/// program to each interface's <em>subnet-directed</em> broadcast
/// address (e.g. <c>192.168.3.255:111</c>), bound to that interface's
/// local address. Limited broadcast (<c>255.255.255.255</c>) only ever
/// egresses one interface on a multi-homed host, so a dedicated probe
/// per NIC is required to reach instruments on a secondary lab subnet.
/// Any host with a VXI-11 server registered answers with the TCP port
/// it listens on; the scanner builds a <c>TCPIP::host::inst0::INSTR</c>
/// resource for each responder.
///
/// Broadcast/multicast discovery is link-local: it cannot cross a router
/// into another subnet, and it only finds instruments that answer a
/// broadcast GETPORT. Those limits are inherent (see ADR 0008).
/// </summary>
public sealed class Vxi11BroadcastScanner : IBackendScanner
{
    private readonly TimeSpan _discoveryWindow;
    private readonly ILogger<Vxi11BroadcastScanner> _logger;

    /// <summary>Creates a scanner that listens for portmapper replies for the supplied window (default 3 s).</summary>
    public Vxi11BroadcastScanner(
        TimeSpan? discoveryWindow = null,
        ILogger<Vxi11BroadcastScanner>? logger = null
    )
    {
        _discoveryWindow = discoveryWindow ?? TimeSpan.FromSeconds(3);
        _logger = logger ?? NullLogger<Vxi11BroadcastScanner>.Instance;
    }

    /// <inheritdoc/>
    public async Task<Result<ImmutableArray<DiscoveredResource>, BackendError>> ScanAsync(
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();

        var targets = EnumerateTargets();
        if (targets.Count == 0)
        {
            return Result.Success<ImmutableArray<DiscoveredResource>, BackendError>(
                ImmutableArray<DiscoveredResource>.Empty
            );
        }

        var responders = new ConcurrentDictionary<IPAddress, int>();
        var xid = unchecked((uint)Random.Shared.Next(int.MinValue, int.MaxValue));
        var request = Vxi11Portmapper.BuildGetportRequest(xid);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_discoveryWindow);

        await Task.WhenAll(targets.Select(t => ProbeAsync(t, request, xid, responders, cts.Token)))
            .ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();

        var resources = responders
            .Keys.Select(BuildDiscovered)
            .Where(r => r is not null)
            .Select(r => r!)
            .ToImmutableArray();

        return Result.Success<ImmutableArray<DiscoveredResource>, BackendError>(resources);
    }

    private async Task ProbeAsync(
        BroadcastTarget target,
        byte[] request,
        uint xid,
        ConcurrentDictionary<IPAddress, int> responders,
        CancellationToken ct
    )
    {
        try
        {
            using var udp = new UdpClient(new IPEndPoint(target.Local, 0))
            {
                EnableBroadcast = true,
            };
            await udp.SendAsync(request, new IPEndPoint(target.Broadcast, PortmapperPort), ct)
                .ConfigureAwait(false);

            while (true)
            {
                UdpReceiveResult datagram;
                try
                {
                    datagram = await udp.ReceiveAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break; // discovery window elapsed
                }
                catch (SocketException ex)
                {
                    _logger.LogDebug(ex, "VXI-11 probe receive failed on {Local}", target.Local);
                    break;
                }

                if (
                    Vxi11Portmapper.TryParseGetportReply(datagram.Buffer, xid, out var port)
                    && port > 0
                )
                {
                    responders[datagram.RemoteEndPoint.Address] = port;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Window elapsed before the send completed — nothing to collect.
        }
        catch (SocketException ex)
        {
            // Binding/sending on this interface failed (e.g. APIPA, tunnel);
            // skip it and let the other interfaces report.
            _logger.LogDebug(ex, "VXI-11 probe failed on interface {Local}", target.Local);
        }
    }

    /// <summary>
    /// Computes the subnet-directed broadcast address for
    /// <paramref name="address"/> under <paramref name="mask"/> by setting
    /// every host bit (e.g. <c>192.168.3.10 / 255.255.255.0</c> →
    /// <c>192.168.3.255</c>).
    /// </summary>
    public static IPAddress DirectedBroadcast(IPAddress address, IPAddress mask)
    {
        var a = address.GetAddressBytes();
        var m = mask.GetAddressBytes();
        var b = new byte[a.Length];
        for (var i = 0; i < b.Length; i++)
        {
            b[i] = (byte)(a[i] | (byte)~m[i]);
        }
        return new IPAddress(b);
    }

    /// <summary>
    /// Decides whether a unicast address belongs on the probe list: it must
    /// sit on an operational, non-loopback interface, be IPv4, and carry a
    /// usable subnet mask.
    /// </summary>
    public static bool ShouldProbe(
        OperationalStatus status,
        NetworkInterfaceType type,
        AddressFamily family,
        IPAddress? mask
    ) =>
        status == OperationalStatus.Up
        && type != NetworkInterfaceType.Loopback
        && family == AddressFamily.InterNetwork
        && mask is not null
        && !mask.Equals(IPAddress.Any);

    private static IReadOnlyList<BroadcastTarget> EnumerateTargets()
    {
        var targets = new List<BroadcastTarget>();
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
                    mask = null; // platform without IPv4 mask info for this address
                }
                if (
                    !ShouldProbe(
                        nic.OperationalStatus,
                        nic.NetworkInterfaceType,
                        addr.Address.AddressFamily,
                        mask
                    )
                )
                {
                    continue;
                }
                targets.Add(
                    new BroadcastTarget(addr.Address, DirectedBroadcast(addr.Address, mask!))
                );
            }
        }
        return targets;
    }

    private static DiscoveredResource? BuildDiscovered(IPAddress host)
    {
        var raw = $"TCPIP0::{host}::inst0::INSTR";
        var parsed = VisaResource.Parse(raw);
        if (parsed is not Result<VisaResource, VisaResourceError>.Ok { Value: var resource })
        {
            return null;
        }
        return new DiscoveredResource(resource, Idn: null);
    }

    private readonly record struct BroadcastTarget(IPAddress Local, IPAddress Broadcast);
}
