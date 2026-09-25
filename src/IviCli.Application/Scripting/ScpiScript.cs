using System.Collections.Immutable;
using System.Globalization;
using IviCli.Domain;
using IviCli.Domain.Scpi;

namespace IviCli.Application.Scripting;

/// <summary>
/// A parsed SCPI script — a sequence of <see cref="ScpiScriptDirective"/>
/// each owning a 1-based source line number. Parsing is a pure function
/// over the raw text; execution is handled by
/// <see cref="ScriptDeviceCommandHandler"/>.
/// </summary>
public sealed record ScpiScript(ImmutableArray<ScpiScriptDirective> Directives)
{
    private const char DirectivePrefix = '!';

    /// <summary>
    /// Parses the supplied script source into a structured
    /// <see cref="ScpiScript"/>. Each line is trimmed and blank lines are
    /// skipped. A line beginning with <c>!</c> is an ivi-cli directive read
    /// whole: <c>!sleep &lt;ms&gt;</c>, <c>!assert &lt;regex&gt;</c> or
    /// <c>!echo &lt;text&gt;</c> (keywords case-insensitive), and <c>!#</c>
    /// starts a comment line. Every other line, including any <c>#</c> in
    /// it, is SCPI sent to the instrument exactly as written: a
    /// <see cref="ScpiScriptDirective.Query"/> when
    /// <see cref="ScpiMessage.IsQuery"/> holds, otherwise a
    /// <see cref="ScpiScriptDirective.Write"/>.
    /// </summary>
    public static Result<ScpiScript, ScpiScriptError> Parse(string source)
    {
        var directives = ImmutableArray.CreateBuilder<ScpiScriptDirective>();
        var lines = source.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var written = lines[i].Trim();
            if (written.Length == 0)
            {
                continue;
            }

            var parsed =
                written[0] == DirectivePrefix
                    ? ParseDirective(written, lineNumber)
                    : Result.Success<ScpiScriptDirective?, ScpiScriptError>(
                        ParseScpi(written, lineNumber)
                    );
            switch (parsed)
            {
                case Result<ScpiScriptDirective?, ScpiScriptError>.Ok { Value: { } directive }:
                    directives.Add(directive);
                    break;
                case Result<ScpiScriptDirective?, ScpiScriptError>.Error error:
                    return Result.Failure<ScpiScript, ScpiScriptError>(error.Err);
                default:
                    break;
            }
        }
        return Result.Success<ScpiScript, ScpiScriptError>(
            new ScpiScript(directives.ToImmutable())
        );
    }

    private static ScpiScriptDirective ParseScpi(string written, int line) =>
        ScpiMessage.IsQuery(written)
            ? new ScpiScriptDirective.Query(line, written)
            : new ScpiScriptDirective.Write(line, written);

    /// <summary>
    /// Reads a <c>!</c>-prefixed line; <c>null</c> for a <c>!#</c> comment.
    /// </summary>
    private static Result<ScpiScriptDirective?, ScpiScriptError> ParseDirective(
        string written,
        int line
    )
    {
        var body = written[1..];
        if (body.StartsWith('#'))
        {
            return Result.Success<ScpiScriptDirective?, ScpiScriptError>(null);
        }
        if (TryReadArgument(body, "sleep", out var sleepArg))
        {
            if (
                !int.TryParse(
                    sleepArg.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var ms
                )
                || ms < 0
            )
            {
                return Invalid(line, written, "sleep argument must be a non-negative integer");
            }
            return Result.Success<ScpiScriptDirective?, ScpiScriptError>(
                new ScpiScriptDirective.Sleep(line, TimeSpan.FromMilliseconds(ms))
            );
        }
        if (TryReadArgument(body, "assert", out var assertArg))
        {
            var pattern = assertArg.Trim();
            if (pattern.Length == 0)
            {
                return Invalid(line, written, "assert requires a regex pattern");
            }
            return Result.Success<ScpiScriptDirective?, ScpiScriptError>(
                new ScpiScriptDirective.Assert(line, pattern)
            );
        }
        if (TryReadArgument(body, "echo", out var text))
        {
            return Result.Success<ScpiScriptDirective?, ScpiScriptError>(
                new ScpiScriptDirective.Echo(line, text)
            );
        }
        return Invalid(line, written, "unknown directive; expected !sleep, !assert, !echo or !#");
    }

    /// <summary>
    /// True when <paramref name="body"/> is <paramref name="keyword"/>
    /// (case-insensitive) followed by a space; <paramref name="argument"/>
    /// is everything after that space.
    /// </summary>
    private static bool TryReadArgument(string body, string keyword, out string argument)
    {
        var prefix = keyword + " ";
        if (body.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            argument = body[prefix.Length..];
            return true;
        }
        argument = string.Empty;
        return false;
    }

    private static Result<ScpiScriptDirective?, ScpiScriptError> Invalid(
        int line,
        string raw,
        string reason
    ) =>
        Result.Failure<ScpiScriptDirective?, ScpiScriptError>(
            new ScpiScriptInvalidDirective(line, raw, reason)
        );
}

/// <summary>A single directive within a parsed script.</summary>
public abstract record ScpiScriptDirective(int Line)
{
    /// <summary>Send a SCPI write command.</summary>
    public sealed record Write(int Line, string Text) : ScpiScriptDirective(Line);

    /// <summary>Send a SCPI query and echo the response.</summary>
    public sealed record Query(int Line, string Text) : ScpiScriptDirective(Line);

    /// <summary>Pause execution.</summary>
    public sealed record Sleep(int Line, TimeSpan Duration) : ScpiScriptDirective(Line);

    /// <summary>Regex-match the most recent query response.</summary>
    public sealed record Assert(int Line, string Pattern) : ScpiScriptDirective(Line);

    /// <summary>Write a literal line to stdout.</summary>
    public sealed record Echo(int Line, string Text) : ScpiScriptDirective(Line);
}

/// <summary>Errors that can arise while parsing a script source.</summary>
public abstract record ScpiScriptError : IviError
{
    /// <inheritdoc/>
    public abstract LogSeverity Severity { get; }

    /// <inheritdoc/>
    public abstract string Message { get; }

    /// <inheritdoc/>
    public virtual IReadOnlyList<object?> LogArgs => Array.Empty<object?>();

    /// <inheritdoc/>
    public virtual Exception? Cause => null;
}

/// <summary>A directive could not be parsed.</summary>
public sealed record ScpiScriptInvalidDirective(int Line, string Raw, string Reason)
    : ScpiScriptError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "script line {Line}: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Line, Reason };
}
