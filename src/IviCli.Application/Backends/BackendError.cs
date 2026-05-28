using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;

namespace IviCli.Application.Backends;

/// <summary>
/// Errors that an <see cref="IIviBackend"/> implementation can surface. Per
/// ADR 0014 §1 these are per-domain sealed variants; per ADR 0014 §9 each
/// carries the logging-contract metadata defined by <see cref="IviError"/>.
/// </summary>
public abstract record BackendError : IviError
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

/// <summary>The Backend timed out waiting on an instrument operation.</summary>
/// <param name="Elapsed">How long the operation waited before timing out.</param>
/// <param name="InnerException">Optional adapter-captured exception, for diagnostic logs only.</param>
public sealed record TransportTimeout(TimeSpan Elapsed, Exception? InnerException = null)
    : BackendError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Error;

    /// <inheritdoc/>
    public override string Message => "transport timeout after {Elapsed}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Elapsed };

    /// <inheritdoc/>
    public override Exception? Cause => InnerException;
}

/// <summary>The Backend lost its connection while attempting an operation.</summary>
/// <param name="Reason">Human-readable reason.</param>
/// <param name="InnerException">Optional adapter-captured exception, for diagnostic logs only.</param>
public sealed record TransportDisconnected(string Reason, Exception? InnerException = null)
    : BackendError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Error;

    /// <inheritdoc/>
    public override string Message => "transport disconnected: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Reason };

    /// <inheritdoc/>
    public override Exception? Cause => InnerException;
}

/// <summary>The instrument did not respond to the request.</summary>
/// <param name="Resource">The VISA resource of the silent device.</param>
public sealed record DeviceNotResponding(VisaResource Resource) : BackendError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Error;

    /// <inheritdoc/>
    public override string Message => "device not responding: {Resource}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Resource.ToLogString() };
}

/// <summary>The Backend cannot perform the requested operation against this device.</summary>
/// <param name="OperationName">Logical op name (e.g. "trigger", "service_request").</param>
/// <param name="DeviceName">The device the op was attempted on.</param>
/// <param name="Reason">Human-readable detail (e.g. "not supported by the SOCKET backend").</param>
public sealed record BackendOperationNotSupported(
    string OperationName,
    DeviceName DeviceName,
    string Reason
) : BackendError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message =>
        "backend does not support {OperationName} on {DeviceName}: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs =>
        new object?[] { OperationName, DeviceName, Reason };
}

/// <summary>The Backend cannot route the request to a compatible transport for this device.</summary>
/// <param name="DeviceName">The device whose transport could not be resolved.</param>
public sealed record UnsupportedTransport(DeviceName DeviceName) : BackendError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Error;

    /// <inheritdoc/>
    public override string Message => "no Backend supports the transport for device {DeviceName}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { DeviceName };
}

/// <summary>
/// The backend session pool (ADR 0038) waited longer than the device's
/// configured timeout for an in-use entry to be released. Distinct from
/// <see cref="TransportTimeout"/> (which means the wire didn't respond)
/// so logs and callers can tell "queued behind another op" apart from
/// "instrument silent."
/// </summary>
/// <param name="Device">The device whose lease wait expired.</param>
/// <param name="Waited">How long the caller waited before giving up.</param>
public sealed record PoolWaitTimeout(DeviceName Device, TimeSpan Waited) : BackendError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message =>
        "pool wait exceeded {Waited} for device {Device}; another op is in flight";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Waited, Device };
}

/// <summary>
/// A mock scenario scene contradicted the operation being performed (e.g. a
/// <c>respond</c> scene matched a <c>WriteAsync</c> call, or an <c>ack</c>
/// scene matched a <c>QueryAsync</c>). Surfaced loudly to make scenario
/// authoring mistakes visible at test time per ADR 0026 §6.
/// </summary>
/// <param name="Match">The matched SCPI text.</param>
/// <param name="Reason">Human-readable description of the contradiction.</param>
public sealed record MockScenarioContractMismatch(string Match, string Reason) : BackendError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Error;

    /// <inheritdoc/>
    public override string Message => "mock scenario mismatch for {Match}: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Match, Reason };
}
