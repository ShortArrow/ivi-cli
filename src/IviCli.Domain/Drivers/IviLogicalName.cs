namespace IviCli.Domain.Drivers;

/// <summary>
/// An IVI logical-name entry as enumerated from the
/// <c>IviConfigurationStore.xml</c> (PRD §6.5 <c>logical list</c>,
/// ADR 0045). Logical names are operator-facing aliases that bind
/// an instrument's hardware asset to a specific driver session;
/// they shield application code from the underlying VISA resource
/// string so the same script targets multiple physical instruments
/// by swapping the store's logical-name → driver-session mapping.
/// </summary>
/// <param name="Name">The logical name as recorded in the store (e.g. <c>"MyScope"</c>).</param>
/// <param name="Description">Human-readable description, when present.</param>
/// <param name="DriverSessionName">
/// The driver session this logical name resolves to, when present —
/// the indirection point that swaps in different hardware without
/// touching application code.
/// </param>
public sealed record IviLogicalName(string Name, string? Description, string? DriverSessionName);
