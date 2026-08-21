using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using IviCli.Application.Scripting;
using IviCli.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace IviCli.Cli.Commands;

/// <summary>
/// Wires the <c>visa lint</c> subcommand (PRD §6.2, ADR 0032). Parses
/// a SCPI script file and emits lint findings (one per line) plus a
/// summary. Exit codes follow ADR 0014: parse / IO failures → usage
/// error, error-severity findings → generic failure, warnings → 0
/// (operator's CI does not break on warnings unless they opt in).
/// </summary>
public static class VisaLintCommand
{
    /// <summary>Builds the configured <see cref="Command"/>.</summary>
    public static Command Build(IServiceProvider services)
    {
        var pathArg = new Argument<string>("path")
        {
            Description = "Path to the .scpi script to lint.",
        };
        var jsonOpt = new Option<bool>("--json")
        {
            Description = "Emit findings as a JSON array on stdout instead of plain text.",
        };

        var command = new Command(
            "lint",
            "Check a SCPI script for unknown command roots without running it."
        );
        command.Arguments.Add(pathArg);
        command.Options.Add(jsonOpt);

        command.SetAction(
            async (parseResult, ct) =>
            {
                var path = parseResult.GetRequiredValue(pathArg);
                var json = parseResult.GetValue(jsonOpt);

                string source;
                try
                {
                    source = await File.ReadAllTextAsync(path, ct);
                }
                catch (Exception ex)
                    when (ex
                            is FileNotFoundException
                                or DirectoryNotFoundException
                                or UnauthorizedAccessException
                                or IOException
                    )
                {
                    Console.Error.WriteLine($"error: cannot read '{path}': {ex.Message}");
                    return ExitCodeMapper.UsageError;
                }

                if (
                    ScpiScript.Parse(source)
                    is not Result<ScpiScript, ScpiScriptError>.Ok { Value: var script }
                )
                {
                    var err = (
                        (Result<ScpiScript, ScpiScriptError>.Error)ScpiScript.Parse(source)
                    ).Err;
                    Console.Error.WriteLine($"error: script could not be parsed: {err.Message}");
                    return ExitCodeMapper.UsageError;
                }

                var linter = services.GetRequiredService<IScriptLinter>();
                var findings = await linter.LintAsync(script, ct);

                if (json)
                {
                    Console.Out.WriteLine(RenderJson(findings));
                }
                else
                {
                    RenderPlain(path, findings, Console.Out);
                }

                return findings.Any(f => f.Severity == LintSeverity.Error)
                    ? ExitCodeMapper.GenericFailure
                    : ExitCodeMapper.Success;
            }
        );

        return command;
    }

    /// <summary>Renders findings as plain text for human consumption.</summary>
    public static void RenderPlain(
        string path,
        System.Collections.Immutable.ImmutableArray<LintFinding> findings,
        TextWriter writer
    )
    {
        if (findings.IsDefaultOrEmpty)
        {
            writer.WriteLine($"{path}: no findings");
            return;
        }
        foreach (var f in findings)
        {
            writer.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{path}:{f.Line}: {f.Severity.ToString().ToLowerInvariant()}: {f.Message} — {f.Snippet}"
                )
            );
        }
        writer.WriteLine($"{path}: {findings.Length} finding(s)");
    }

    /// <summary>Renders findings as a JSON array for tooling consumption.</summary>
    public static string RenderJson(
        System.Collections.Immutable.ImmutableArray<LintFinding> findings
    )
    {
        var dto = findings
            .Select(f => new LintFindingView(
                f.Line,
                f.Severity.ToString().ToLowerInvariant(),
                f.Message,
                f.Snippet
            ))
            .ToArray();
        return JsonSerializer.Serialize(dto, CliJsonContext.Default.LintFindingViewArray);
    }
}
