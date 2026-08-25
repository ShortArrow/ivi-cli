using System.CommandLine;
using System.CommandLine.Help;

namespace IviCli.Cli;

/// <summary>
/// Decides whether an invocation should activate the scenarios named by
/// <c>IVICLI_SCENARIO</c> and by <c>session.json</c> before running.
/// </summary>
/// <remarks>
/// Activation reads the scenario store and warns about bindings it cannot
/// load. That is worth saying when a command is about to talk to a device
/// and noise when it is not: <c>ivicli --version</c> printed the warning
/// ahead of the version string, and anything parsing that output saw it.
/// </remarks>
public static class ScenarioActivation
{
    /// <summary>
    /// True when <paramref name="parseResult"/> resolves to a command that
    /// can reach a backend. Help output, version output, and a command line
    /// that failed to parse resolve to false — none of them opens a session,
    /// so none of them needs the scenario store read.
    /// </summary>
    public static bool IsNeededFor(ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        if (parseResult.Errors.Count > 0)
        {
            return false;
        }
        // Asking the parser what it resolved, rather than matching raw
        // tokens: an argument that merely looks like `--help` belongs to the
        // command that takes it, and only the real thing produces a
        // HelpAction.
        if (parseResult.Action is HelpAction)
        {
            return false;
        }
        // The version option's action type is not public, so the option
        // itself is the handle. It lives on the root command, which is where
        // `--version` is accepted.
        var version = parseResult
            .RootCommandResult.Command.Options.OfType<VersionOption>()
            .FirstOrDefault();
        return version is null || parseResult.GetResult(version) is null;
    }
}
