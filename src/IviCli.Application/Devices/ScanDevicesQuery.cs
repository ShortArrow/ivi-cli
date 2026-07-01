using System.Collections.Immutable;
using IviCli.Application.Backends;
using IviCli.Domain;

namespace IviCli.Application.Devices;

/// <summary>Query DTO for the <c>visa scan</c> command (PRD §6.2).</summary>
/// <param name="Options">Sweep ports, target overrides, and verbosity for this scan.</param>
public sealed record ScanDevicesQuery(ScanOptions Options)
{
    /// <summary>A passive scan with default options.</summary>
    public ScanDevicesQuery()
        : this(ScanOptions.Default) { }
}

/// <summary>The aggregated discovery result.</summary>
/// <param name="Resources">The discovered resources, indexed by their position in the array.</param>
public sealed record ScanResult(ImmutableArray<DiscoveredResource> Resources);

/// <summary>Outcomes the <c>visa scan</c> query can fail with.</summary>
public abstract record ScanDevicesError : IviError
{
    /// <inheritdoc/>
    public abstract LogSeverity Severity { get; }

    /// <inheritdoc/>
    public abstract string Message { get; }

    /// <inheritdoc/>
    public virtual IReadOnlyList<object?> LogArgs => Array.Empty<object?>();

    /// <inheritdoc/>
    public virtual Exception? Cause => null;
}

/// <summary>A scanner reported a transport-level failure.</summary>
public sealed record ScanDevicesScannerFailure(BackendError Inner) : ScanDevicesError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => Inner.Severity;

    /// <inheritdoc/>
    public override string Message => Inner.Message;

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => Inner.LogArgs;

    /// <inheritdoc/>
    public override Exception? Cause => Inner.Cause;
}
