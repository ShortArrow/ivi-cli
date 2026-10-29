using IviCli.Application.Scripting;

namespace IviCli.Cli.Commands;

/// <summary>
/// Phrases a script that could not be read for the person who wrote it:
/// the line, and why it was refused.
/// </summary>
public static class ScriptParseMessage
{
    /// <summary>Returns the console line for <paramref name="error"/>.</summary>
    public static string For(ScpiScriptError error) =>
        error is ScpiScriptInvalidDirective d
            ? $"error: script line {d.Line}: {d.Reason}."
            : "error: script parse failed.";
}
