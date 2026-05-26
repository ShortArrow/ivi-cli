using System.CommandLine;

namespace IviCli.Cli.Commands;

/// <summary>
/// Wires <c>ivicli completion &lt;shell&gt;</c> — emits the shell stub
/// that drives runtime completion via the hidden <c>__complete</c>
/// subcommand. The script is short and shell-specific; the actual
/// candidate enumeration happens in-process when the shell calls back
/// into the CLI.
/// </summary>
public static class CompletionCommand
{
    private const string ProgramName = "ivicli";

    /// <summary>Builds the configured <see cref="Command"/>.</summary>
    public static Command Build()
    {
        var shellArg = new Argument<string>("shell")
        {
            Description = "Target shell: bash, zsh, or powershell.",
        };

        var command = new Command(
            "completion",
            "Emit a completion script for bash / zsh / PowerShell. Source it from your shell rc file."
        );
        command.Arguments.Add(shellArg);

        command.SetAction(parseResult =>
        {
            var shell = parseResult.GetRequiredValue(shellArg).ToLowerInvariant();
            switch (shell)
            {
                case "bash":
                    Console.Write(BashScript);
                    return ExitCodeMapper.Success;
                case "zsh":
                    Console.Write(ZshScript);
                    return ExitCodeMapper.Success;
                case "powershell":
                case "pwsh":
                    Console.Write(PowerShellScript);
                    return ExitCodeMapper.Success;
                default:
                    Console.Error.WriteLine(
                        $"error: unknown shell '{shell}'. Supported: bash, zsh, powershell."
                    );
                    return ExitCodeMapper.UsageError;
            }
        });

        return command;
    }

    private const string BashScript = """
        # ivicli bash completion
        # Source: eval "$(ivicli completion bash)"
        _ivicli_complete() {
            local cur line
            cur="${COMP_WORDS[COMP_CWORD]}"
            line="${COMP_LINE}"
            local IFS=$'\n'
            COMPREPLY=($(ivicli __complete "${line}" 2>/dev/null))
            return 0
        }
        complete -F _ivicli_complete ivicli

        """;

    private const string ZshScript = """
        # ivicli zsh completion
        # Source: eval "$(ivicli completion zsh)"
        _ivicli() {
            local -a candidates
            local line="${BUFFER}"
            candidates=(${(f)"$(ivicli __complete "${line}" 2>/dev/null)"})
            compadd -a candidates
        }
        compdef _ivicli ivicli

        """;

    private const string PowerShellScript = """
        # ivicli PowerShell completion
        # Source: ivicli completion powershell | Out-String | Invoke-Expression
        Register-ArgumentCompleter -Native -CommandName ivicli -ScriptBlock {
            param($wordToComplete, $commandAst, $cursorPosition)
            $line = $commandAst.ToString()
            $candidates = & ivicli __complete "$line" 2>$null
            foreach ($c in $candidates) {
                [System.Management.Automation.CompletionResult]::new($c, $c, 'ParameterValue', $c)
            }
        }

        """;
}
