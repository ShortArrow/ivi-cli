using System.CommandLine;
using IviCli.Cli.Completion;
using Microsoft.Extensions.DependencyInjection;

namespace IviCli.Cli.Commands;

/// <summary>
/// Hidden internal verb invoked by shell completion stubs. Given the
/// raw command line the user has typed so far, emits one candidate per
/// line on stdout. The bash / zsh / PowerShell stubs from
/// <c>ivicli completion</c> consume that output.
/// </summary>
public static class CompleteCommand
{
    /// <summary>
    /// Builds the hidden completion driver command. The supplied
    /// service provider is consulted for the
    /// <see cref="CompletionRegistry"/>; concrete
    /// <see cref="IDynamicCompleter"/>s are registered separately
    /// (typically by each verb's Build method).
    /// </summary>
    public static Command Build(RootCommand root, IServiceProvider services)
    {
        var lineArg = new Argument<string>("line")
        {
            Description = "The full command line the user has typed (including 'ivicli').",
        };

        var command = new Command(
            "__complete",
            "(internal) Emit completion candidates for the supplied command line."
        )
        {
            Hidden = true,
        };
        command.Arguments.Add(lineArg);

        command.SetAction(
            async (parseResult, ct) =>
            {
                var line = parseResult.GetRequiredValue(lineArg);
                var tokens = CommandTreeWalker.Tokenize(line);
                // The first token is the program name (`ivicli`); drop
                // it so walker semantics line up with shell-supplied
                // COMP_WORDS.
                var withoutProgram = tokens.Length > 0 ? tokens.RemoveAt(0) : tokens;
                var registry = services.GetRequiredService<CompletionRegistry>();
                var candidates = await CommandTreeWalker.CompleteAsync(
                    root,
                    withoutProgram,
                    registry,
                    ct
                );
                foreach (var c in candidates)
                {
                    Console.WriteLine(c);
                }
                return ExitCodeMapper.Success;
            }
        );

        return command;
    }
}
