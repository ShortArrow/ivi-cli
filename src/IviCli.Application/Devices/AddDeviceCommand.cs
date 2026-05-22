namespace IviCli.Application.Devices;

/// <summary>
/// Command DTO for adding a new device to the configuration. The fields are
/// the raw, untyped form as supplied by the CLI; the handler is responsible
/// for parsing them into Domain Value Objects (per ADR 0003 §2 Anti-Corruption
/// Layer).
/// </summary>
/// <param name="Name">Candidate device alias.</param>
/// <param name="Resource">Candidate VISA resource string.</param>
/// <param name="TimeoutMilliseconds">Per-device default timeout, in milliseconds.</param>
public sealed record AddDeviceCommand(string Name, string Resource, int TimeoutMilliseconds);
