using System.Collections.Immutable;
using IviCli.Application.Configuration;
using IviCli.Application.Devices;
using IviCli.Domain;
using IviCli.Domain.Devices;

namespace IviCli.Application.Watch;

/// <summary>
/// Command DTO for <c>visa watch</c>: periodically probe a set of
/// registered devices and emit each tick via <see cref="IWatchDevicesSink"/>.
/// </summary>
/// <param name="Names">
/// Optional explicit subset of device aliases. <see langword="null"/> or
/// empty means "every device in <see cref="Domain.Configuration.ConfigDocument.Devices"/>".
/// </param>
/// <param name="Interval">Delay between successive ticks (must be &gt; 0).</param>
/// <param name="MaxIterations">Optional bound on the number of ticks emitted.</param>
public sealed record WatchDevicesCommand(
    ImmutableArray<string>? Names,
    TimeSpan Interval,
    int? MaxIterations
);

/// <summary>
/// One periodic snapshot emitted by <see cref="WatchDevicesCommandHandler"/>.
/// The sink receives one of these per tick and is responsible for
/// presentation (live TUI table, NDJSON line, plain-text snapshot).
/// </summary>
/// <param name="Timestamp">Wall-clock time the snapshot was assembled.</param>
/// <param name="Sequence">Zero-based tick counter.</param>
/// <param name="Snapshots">Per-device status, in the order supplied to the handler.</param>
public sealed record WatchTick(
    DateTimeOffset Timestamp,
    int Sequence,
    ImmutableArray<DeviceStatus> Snapshots
);

/// <summary>
/// Application-side sink port for <c>visa watch</c>. The CLI provides the
/// concrete renderer (Spectre.Console live table, NDJSON, plain text);
/// Application code remains free of any presentation dependency.
/// </summary>
public interface IWatchDevicesSink
{
    /// <summary>Receives one snapshot tick. Implementations should not throw on cancellation.</summary>
    Task EmitAsync(WatchTick tick, CancellationToken ct);
}

/// <summary>Outcomes the watch handler can fail with at the Application level.</summary>
public abstract record WatchDevicesError : IviError
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

/// <summary>One of the supplied device-alias arguments could not be parsed.</summary>
public sealed record WatchInvalidName(string Raw) : WatchDevicesError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid device name: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The named device is not registered.</summary>
public sealed record WatchUnknownDevice(DeviceName Name) : WatchDevicesError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "device not found: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>No registered devices to watch (bare invocation against an empty config).</summary>
public sealed record WatchNoDevices : WatchDevicesError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "no devices registered to watch";
}

/// <summary>The supplied interval is zero or negative.</summary>
public sealed record WatchInvalidInterval(TimeSpan Given) : WatchDevicesError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "interval must be positive (got {Given})";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Given };
}

/// <summary>The configuration store could not be read.</summary>
public sealed record WatchConfigFailure(ConfigStoreError Inner) : WatchDevicesError
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
