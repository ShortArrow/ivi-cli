namespace IviCli.Application.Backends;

/// <summary>
/// Probes a single TCP endpoint for the <c>visa scan</c> sweep and
/// host-enrichment passes (ADR 0008): a bounded-timeout connect that
/// optionally sends <c>*IDN?</c> to confirm the port speaks SCPI. Keeps the
/// socket round-trip behind an abstraction so the discovery pipeline stays
/// unit-testable with a fake prober.
/// </summary>
public interface IEndpointProber
{
    /// <summary>
    /// Attempts a TCP connection to <paramref name="host"/>:<paramref name="port"/>.
    /// When <paramref name="identify"/> is set and the connection succeeds, sends
    /// <c>*IDN?</c> and captures the response.
    /// </summary>
    Task<EndpointProbe> ProbeAsync(string host, int port, bool identify, CancellationToken ct);
}

/// <summary>The outcome of a single endpoint probe.</summary>
/// <param name="Open">Whether the TCP connection succeeded.</param>
/// <param name="Idn">
/// The trimmed <c>*IDN?</c> response when identification was requested and the
/// endpoint answered; <see langword="null"/> otherwise.
/// </param>
public sealed record EndpointProbe(bool Open, string? Idn);
