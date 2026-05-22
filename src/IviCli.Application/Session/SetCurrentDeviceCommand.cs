namespace IviCli.Application.Session;

/// <summary>
/// Command DTO for setting the current device alias (PRD §6.2).
/// </summary>
/// <param name="Name">Candidate device alias to make current.</param>
/// <param name="Persist">
/// When <see langword="true"/>, also writes the alias as the default device
/// in <c>config.toml</c> (the <c>--default</c> CLI flag).
/// </param>
public sealed record SetCurrentDeviceCommand(string Name, bool Persist);
