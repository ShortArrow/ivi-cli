using System.Collections.Immutable;
using IviCli.Application.Backends;
using IviCli.Domain;

namespace IviCli.Application.Devices;

/// <summary>
/// Application-layer handler for <c>visa scan</c>. Iterates every registered
/// <see cref="IBackendScanner"/>, aggregates their results, and surfaces the
/// first transport failure (subsequent scanners are still tried to keep the
/// CLI responsive when one backend is misbehaving — they just contribute
/// nothing on failure).
/// </summary>
public sealed class ScanDevicesQueryHandler
{
    private readonly IEnumerable<IBackendScanner> _scanners;

    /// <summary>Creates a handler bound to the supplied scanners.</summary>
    public ScanDevicesQueryHandler(IEnumerable<IBackendScanner> scanners)
    {
        _scanners = scanners;
    }

    /// <summary>Performs the aggregated scan.</summary>
    public async Task<Result<ScanResult, ScanDevicesError>> HandleAsync(
        ScanDevicesQuery query,
        CancellationToken ct
    )
    {
        var collected = ImmutableArray.CreateBuilder<DiscoveredResource>();
        ScanDevicesError? firstError = null;

        foreach (var scanner in _scanners)
        {
            ct.ThrowIfCancellationRequested();
            var scanResult = await scanner.ScanAsync(ct);
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

        return Result.Success<ScanResult, ScanDevicesError>(
            new ScanResult(collected.ToImmutable())
        );
    }
}
