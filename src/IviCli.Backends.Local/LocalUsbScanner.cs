using System.Collections.Immutable;
using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Visa;

namespace IviCli.Backends.Local;

/// <summary>
/// Discovery scanner for USB-attached instruments. Enumerates
/// <c>USB?*::INSTR</c> through the installed VISA runtime and reports each
/// resource it can parse, without probing for <c>*IDN?</c>. Machines without
/// a VISA runtime simply contribute no USB entries to <c>visa scan</c>.
/// </summary>
public sealed class LocalUsbScanner : IBackendScanner
{
    private const string UsbInstrPattern = "USB?*::INSTR";

    private readonly IVisaResourceFinder _finder;

    /// <summary>Creates a scanner bound to the supplied resource finder.</summary>
    public LocalUsbScanner(IVisaResourceFinder finder)
    {
        _finder = finder;
    }

    /// <inheritdoc/>
    public Task<Result<ImmutableArray<DiscoveredResource>, BackendError>> ScanAsync(
        ScanOptions options,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();

        // A VISA runtime reports "no resources found" as an exception, which
        // is indistinguishable from a genuine fault once it has crossed the
        // reflective boundary. Discovery is best-effort and `ivicli doctor`
        // owns runtime diagnostics, so every failure yields no entries rather
        // than an error that could blank an otherwise useful scan.
        var discovered = _finder.Find(UsbInstrPattern)
            is Result<ImmutableArray<string>, LocalVisaError>.Ok { Value: var resources }
            ? UsbResourcesOf(resources)
            : ImmutableArray<DiscoveredResource>.Empty;

        return Task.FromResult(
            Result.Success<ImmutableArray<DiscoveredResource>, BackendError>(discovered)
        );
    }

    private static ImmutableArray<DiscoveredResource> UsbResourcesOf(
        ImmutableArray<string> resourceStrings
    ) =>
        resourceStrings
            .Select(VisaResource.Parse)
            .OfType<Result<VisaResource, VisaResourceError>.Ok>()
            .Select(ok => ok.Value)
            .OfType<VisaResource.Usb>()
            .Select(usb => new DiscoveredResource(usb, Idn: null))
            .ToImmutableArray();
}
