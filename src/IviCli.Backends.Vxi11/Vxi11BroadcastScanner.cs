using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Visa;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static IviCli.Domain.Protocols.Vxi11Constants;

namespace IviCli.Backends.Vxi11;

/// <summary>
/// VXI-11 portmapper broadcast scanner (ADR 0008, Batch W).
///
/// Sends an ONC RPC <c>PMAPPROC_GETPORT</c> request asking for the
/// VXI-11 Device Core program (0x0607AF) over UDP broadcast
/// (255.255.255.255:111). Any host with a VXI-11 server registered
/// answers with the TCP port it listens on; the scanner builds a
/// <c>TCPIP::host::inst0::INSTR</c> resource for each responder.
///
/// Discovery window is bounded by a configurable timeout (default
/// 3 s). The scanner intentionally does not chase the per-host TCP
/// port (e.g. by issuing a follow-up <c>create_link</c>) — the
/// presence of a portmapper registration is sufficient evidence
/// that <c>ivicli visa add</c> + the standard backend dispatch will
/// reach the instrument.
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

        var responders = new ConcurrentDictionary<IPAddress, int>();

        using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };

        // Bind to an ephemeral port so replies have somewhere to land.
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        var xid = unchecked((uint)Random.Shared.Next(int.MinValue, int.MaxValue));
        var request = Vxi11Portmapper.BuildGetportRequest(xid);

        try
        {
            await udp.SendAsync(request, new IPEndPoint(IPAddress.Broadcast, PortmapperPort), ct)
                .ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            _logger.LogDebug(ex, "VXI-11 broadcast send failed");
            return Result.Success<ImmutableArray<DiscoveredResource>, BackendError>(
                ImmutableArray<DiscoveredResource>.Empty
            );
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_discoveryWindow);

        try
        {
            while (true)
            {
                cts.Token.ThrowIfCancellationRequested();
                UdpReceiveResult datagram;
                try
                {
                    datagram = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    _logger.LogDebug(ex, "VXI-11 broadcast receive failed");
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
        finally
        {
            // UdpClient.Dispose handles socket teardown.
        }

        var resources = responders
            .Select(kvp => BuildDiscovered(kvp.Key))
            .Where(r => r is not null)
            .Select(r => r!)
            .ToImmutableArray();

        return Result.Success<ImmutableArray<DiscoveredResource>, BackendError>(resources);
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
}
