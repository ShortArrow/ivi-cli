using IviCli.Domain.Devices;

namespace IviCli.Application.Backends;

/// <summary>
/// A push event a backend emits when the underlying instrument
/// raises its Service Request (SRQ) line (ADR 0041).
/// </summary>
/// <param name="Device">The device whose backend reported the SRQ.</param>
/// <param name="StatusByte">
/// Best-effort copy of the instrument's Status Byte register; zero
/// when the backend cannot poll it (e.g. when the SRQ was synthesised
/// for testing or replayed from a recording).
/// </param>
/// <param name="Timestamp">When the backend observed the SRQ (UTC).</param>
public sealed record ServiceRequest(DeviceName Device, byte StatusByte, DateTimeOffset Timestamp);
