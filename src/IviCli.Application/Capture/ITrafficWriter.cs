namespace IviCli.Application.Capture;

/// <summary>
/// Application-side sink port for VISA traffic capture (ADR 0031).
/// One <see cref="TrafficEvent"/> per backend operation (open / close /
/// write / query / read). The Infrastructure adapter persists events;
/// Application code only consumes this interface so no IO concern
/// leaks across the layer boundary.
/// </summary>
public interface ITrafficWriter
{
    /// <summary>Persists <paramref name="ev"/>. Implementations may swallow IO errors.</summary>
    Task AppendAsync(TrafficEvent ev, CancellationToken ct);
}

/// <summary>One captured backend operation, persisted as one NDJSON line.</summary>
/// <param name="Timestamp">UTC time the operation completed.</param>
/// <param name="Device">Device alias the operation targeted.</param>
/// <param name="Op">Which method on <c>IIviBackend</c> the event represents.</param>
/// <param name="Data">SCPI command / query text. <see langword="null"/> for Open / Close / Read.</param>
/// <param name="Response">Backend response. <see langword="null"/> for Open / Close / Write.</param>
/// <param name="Ok">True when the underlying call succeeded.</param>
/// <param name="LatencyMs">Round-trip duration of the call when latency is meaningful.</param>
/// <param name="Error">Failure message when <see cref="Ok"/> is false.</param>
public sealed record TrafficEvent(
    DateTimeOffset Timestamp,
    string Device,
    TrafficOp Op,
    string? Data,
    string? Response,
    bool Ok,
    int? LatencyMs,
    string? Error
);

/// <summary>Which <c>IIviBackend</c> method an event represents.</summary>
public enum TrafficOp
{
    /// <summary>Open the session.</summary>
    Open,

    /// <summary>Close the session.</summary>
    Close,

    /// <summary>Send a SCPI command (no expected response).</summary>
    Write,

    /// <summary>Send a SCPI query and read the response.</summary>
    Query,

    /// <summary>Read whatever the backend has buffered.</summary>
    Read,

    /// <summary>Assert a hardware trigger (ADR 0041).</summary>
    Trigger,
}

/// <summary>
/// No-op writer used when traffic capture is disabled (the default).
/// Singleton so DI registrations can take a non-nullable port.
/// </summary>
public sealed class NullTrafficWriter : ITrafficWriter
{
    /// <summary>Shared singleton.</summary>
    public static readonly NullTrafficWriter Instance = new();

    private NullTrafficWriter() { }

    /// <inheritdoc/>
    public Task AppendAsync(TrafficEvent ev, CancellationToken ct) => Task.CompletedTask;
}
