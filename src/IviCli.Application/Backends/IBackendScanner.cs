using System.Collections.Immutable;
using IviCli.Domain;
using IviCli.Domain.Visa;

namespace IviCli.Application.Backends;

/// <summary>
/// A backend that can enumerate the VISA resources it is currently able to
/// see (per PRD §6.2 <c>visa scan</c>). Each <see cref="IIviBackend"/> may
/// implement this when discovery is meaningful for its transport; for
/// transports without discovery (e.g. raw SOCKET) the scanner simply
/// returns an empty list.
/// </summary>
public interface IBackendScanner
{
    /// <summary>
    /// Enumerates the resources discoverable through this Backend under the
    /// supplied <paramref name="options"/> (sweep ports, target overrides,
    /// verbosity). Scanners ignore options that do not apply to them.
    /// </summary>
    Task<Result<ImmutableArray<DiscoveredResource>, BackendError>> ScanAsync(
        ScanOptions options,
        CancellationToken ct
    );
}

/// <summary>A single discovered VISA endpoint, ready to be registered via <c>visa add</c>.</summary>
/// <param name="Resource">The discovered VISA resource.</param>
/// <param name="Idn">
/// The instrument's <c>*IDN?</c> response when the scanner probed it; <see langword="null"/>
/// when the scanner cannot or chose not to probe.
/// </param>
/// <param name="Detail">
/// Optional human-readable diagnostic shown only under <c>--verbose</c> (e.g. the VXI-11
/// Device Core port resolved via the portmapper); <see langword="null"/> when there is none.
/// </param>
public sealed record DiscoveredResource(VisaResource Resource, string? Idn, string? Detail = null);
