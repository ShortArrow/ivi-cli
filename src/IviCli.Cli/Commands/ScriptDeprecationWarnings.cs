using IviCli.Application.Scripting;
using IviCli.Domain;

namespace IviCli.Cli.Commands;

/// <summary>
/// Tells the author of a script which lines use the form 0.4.0 removes.
/// Printed before the script runs, so the warnings survive a run that
/// fails; a script that does not parse prints nothing here, because the
/// command reports the parse error itself.
/// </summary>
public static class ScriptDeprecationWarnings
{
    /// <summary>Writes one <c>warning:</c> line per deprecated line of <paramref name="source"/>.</summary>
    public static void Write(string source, TextWriter writer)
    {
        if (
            ScpiScript.Parse(source)
            is not Result<ScpiScript, ScpiScriptError>.Ok { Value: var script }
        )
        {
            return;
        }
        foreach (var deprecation in script.Deprecations)
        {
            writer.WriteLine($"warning: line {deprecation.Line}: {deprecation.Message}");
        }
    }
}
