namespace IviCli.Application.Devices;

/// <summary>
/// Command DTO for removing an existing device alias from the configuration.
/// The field is the raw, untyped form as supplied by the CLI; the handler
/// validates it into a Domain Value Object before applying the change.
/// </summary>
/// <param name="Name">Candidate device alias to remove.</param>
public sealed record RemoveDeviceCommand(string Name);
