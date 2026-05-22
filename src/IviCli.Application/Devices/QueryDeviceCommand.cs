namespace IviCli.Application.Devices;

/// <summary>
/// Command DTO for <c>visa query</c> (PRD §6.2). When <see cref="Name"/> is
/// null the handler resolves the target via the session / config defaults.
/// </summary>
/// <param name="Name">Optional device alias; <see langword="null"/> uses the current device.</param>
/// <param name="ScpiText">The raw SCPI query string.</param>
public sealed record QueryDeviceCommand(string? Name, string ScpiText);
