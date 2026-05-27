using System.Collections.Immutable;
using IviCli.Domain.Scpi;

namespace IviCli.Application.Scripting;

/// <summary>
/// Application-layer port that inspects an already-parsed
/// <see cref="ScpiScript"/> and reports lint findings (ADR 0032). v1
/// flags only unknown SCPI command roots; future rule sets (parameter
/// syntax, vendor extensions) can land behind this port without
/// touching callers.
/// </summary>
public interface IScriptLinter
{
    /// <summary>Walks <paramref name="script"/> and returns one finding per issue.</summary>
    Task<ImmutableArray<LintFinding>> LintAsync(ScpiScript script, CancellationToken ct);
}

/// <summary>A single SCPI lint issue tied to a source line.</summary>
/// <param name="Line">1-based line number where the issue lives.</param>
/// <param name="Severity">How the renderer should classify the finding.</param>
/// <param name="Message">User-facing message (no template placeholders).</param>
/// <param name="Snippet">The offending command text (truncated to 80 chars with an ellipsis).</param>
public sealed record LintFinding(int Line, LintSeverity Severity, string Message, string Snippet);

/// <summary>How the CLI renderer should classify a <see cref="LintFinding"/>.</summary>
public enum LintSeverity
{
    /// <summary>Informational, never affects exit codes.</summary>
    Info,

    /// <summary>Likely bug; surfaced to the operator but not fatal.</summary>
    Warning,

    /// <summary>Hard error; the renderer maps this to a non-zero exit code.</summary>
    Error,
}

/// <summary>
/// Default linter: walks every <c>Write</c> / <c>Query</c> directive and
/// flags those whose root mnemonic is not in <see cref="ScpiVocabulary"/>.
/// Sleep / Assert / Echo directives are control flow, not SCPI, and never
/// produce findings.
/// </summary>
public sealed class DefaultScriptLinter : IScriptLinter
{
    private const int SnippetLimit = 80;

    /// <inheritdoc/>
    public Task<ImmutableArray<LintFinding>> LintAsync(ScpiScript script, CancellationToken ct)
    {
        var findings = ImmutableArray.CreateBuilder<LintFinding>();
        foreach (var directive in script.Directives)
        {
            ct.ThrowIfCancellationRequested();
            var text = directive switch
            {
                ScpiScriptDirective.Write w => w.Text,
                ScpiScriptDirective.Query q => q.Text,
                _ => null,
            };
            if (text is null)
            {
                continue;
            }
            if (ScpiVocabulary.IsKnownRoot(text))
            {
                continue;
            }
            var root = ScpiVocabulary.ExtractRoot(text) ?? "(empty)";
            findings.Add(
                new LintFinding(
                    directive.Line,
                    LintSeverity.Warning,
                    $"unknown SCPI root: '{root}'",
                    Snippet(text)
                )
            );
        }
        return Task.FromResult(findings.ToImmutable());
    }

    private static string Snippet(string raw)
    {
        if (raw.Length <= SnippetLimit)
        {
            return raw;
        }
        return string.Concat(raw.AsSpan(0, SnippetLimit - 1), "…");
    }
}
