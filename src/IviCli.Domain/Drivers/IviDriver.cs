namespace IviCli.Domain.Drivers;

/// <summary>
/// An IVI driver entry as enumerated from the
/// <c>IviConfigurationStore.xml</c> (PRD §6.5 <c>driver list</c>,
/// ADR 0045). Each field is optional except <see cref="Name"/>: the
/// store sometimes omits descriptive metadata or the module path on
/// older installations.
/// </summary>
/// <param name="Name">
/// The software-module name as recorded in the store
/// (e.g. <c>"IviScope"</c>, <c>"AgN9020A"</c>).
/// </param>
/// <param name="Description">Human-readable description, when present.</param>
/// <param name="ModulePath">
/// Filesystem path of the driver assembly (typically a vendor DLL),
/// when present. Used by IVI runtimes to locate the driver at session
/// open.
/// </param>
/// <param name="Prefix">
/// The driver's COM / .NET class-name prefix (e.g. <c>"Ag344xx"</c>),
/// when present.
/// </param>
public sealed record IviDriver(
    string Name,
    string? Description,
    string? ModulePath,
    string? Prefix
);
