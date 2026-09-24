using System.CommandLine;

namespace IviCli.Cli.Commands;

/// <summary>
/// The logging flags every command accepts: <c>-v</c>/<c>--verbose</c>,
/// <c>-vv</c>, <c>-q</c>/<c>--quiet</c>, <c>--log-format</c> and
/// <c>--log-file</c>. <c>Program.Main</c> reads their values before the
/// logger exists; they are declared here so the parser accepts them too,
/// before or after any subcommand.
/// </summary>
public static class GlobalLoggingOptions
{
    /// <summary>Adds the logging flags to <paramref name="root"/>, recursively.</summary>
    public static void AddTo(RootCommand root)
    {
        root.Options.Add(
            new Option<bool>("--verbose", "-v")
            {
                Description = "Log at Debug and above.",
                Recursive = true,
            }
        );
        root.Options.Add(
            new Option<bool>("-vv") { Description = "Log at Trace and above.", Recursive = true }
        );
        root.Options.Add(
            new Option<bool>("--quiet", "-q")
            {
                Description = "Suppress console output below Warning; the log file is unaffected.",
                Recursive = true,
            }
        );
        root.Options.Add(
            new Option<string>("--log-format")
            {
                Description = "Console log format: human (default) or json.",
                Recursive = true,
            }
        );
        root.Options.Add(
            new Option<string>("--log-file")
            {
                Description = "Write the log file here instead of the default location.",
                Recursive = true,
            }
        );
    }
}
