using System.Collections.Immutable;
using System.Globalization;
using IviCli.Domain;

namespace IviCli.Application.Scripting;

/// <summary>
/// A parsed SCPI script — a sequence of <see cref="ScpiScriptDirective"/>
/// each owning a 1-based source line number. Parsing is a pure function
/// over the raw text; execution is handled by
/// <see cref="ScriptDeviceCommandHandler"/>.
/// </summary>
public sealed record ScpiScript(ImmutableArray<ScpiScriptDirective> Directives)
{
    /// <summary>
    /// Lines written in the form 0.4.0 removes: a directive without the
    /// <c>!</c> prefix, or a <c>#</c> comment. They still parse as before;
    /// each one is listed here so the caller can say where the script needs
    /// changing.
    /// </summary>
    public ImmutableArray<ScpiScriptDeprecation> Deprecations { get; init; } = [];

    /// <summary>
    /// Parses the supplied script source into a structured
    /// <see cref="ScpiScript"/>. A line starting with <c>!</c> is a directive
    /// (<c>!sleep</c>, <c>!assert</c>, <c>!echo</c>) or, as <c>!#</c>, a
    /// comment, and is read whole. Any other line is read as before: blank
    /// lines and <c>#</c> comments are skipped, a trailing <c># ...</c> is
    /// stripped, and an unprefixed <c>sleep</c>, <c>assert</c> or <c>echo</c>
    /// is a directive; each such use is recorded in
    /// <see cref="Deprecations"/>.
    /// </summary>
    public static Result<ScpiScript, ScpiScriptError> Parse(string source)
    {
        var directives = ImmutableArray.CreateBuilder<ScpiScriptDirective>();
        var deprecations = ImmutableArray.CreateBuilder<ScpiScriptDeprecation>();
        var lines = source.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var written = lines[i].Trim();
            var parsed = written.StartsWith('!')
                ? ParsePrefixed(written, lineNumber)
                : ParseUnprefixed(written, lineNumber, deprecations);
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
            new ScpiScript(directives.ToImmutable()) { Deprecations = deprecations.ToImmutable() }
        );
    }

    private static Result<ScpiScriptDirective?, ScpiScriptError> ParsePrefixed(
        string written,
        int line
    )
    {
        var body = written[1..];
        if (body.StartsWith('#'))
        {
            return Result.Success<ScpiScriptDirective?, ScpiScriptError>(null);
        }
        if (TryParseKeyword(body, line) is { } keyword)
        {
            return keyword;
        }
        return Result.Failure<ScpiScriptDirective?, ScpiScriptError>(
            new ScpiScriptInvalidDirective(
                line,
                written,
                "unknown directive; expected !sleep, !assert, !echo or !#"
            )
        );
    }

    private static Result<ScpiScriptDirective?, ScpiScriptError> ParseUnprefixed(
        string written,
        int line,
        ImmutableArray<ScpiScriptDeprecation>.Builder deprecations
    )
    {
        var hash = written.IndexOf('#');
        var raw = (hash < 0 ? written : written[..hash]).Trim();
        if (hash >= 0)
        {
            deprecations.Add(ScpiScriptDeprecation.ForComment(line, written, raw, written[hash..]));
        }
        if (raw.Length == 0)
        {
            return Result.Success<ScpiScriptDirective?, ScpiScriptError>(null);
        }
        if (TryParseKeyword(raw, line) is { } keyword)
        {
            if (keyword is Result<ScpiScriptDirective?, ScpiScriptError>.Ok)
            {
                deprecations.Add(ScpiScriptDeprecation.ForDirective(line, raw));
            }
            return keyword;
        }
        return Result.Success<ScpiScriptDirective?, ScpiScriptError>(
            raw.EndsWith('?')
                ? new ScpiScriptDirective.Query(line, raw)
                : new ScpiScriptDirective.Write(line, raw)
        );
    }

    /// <summary>
    /// Reads <paramref name="text"/> as <c>sleep</c>, <c>assert</c> or
    /// <c>echo</c>, the directive names shared by both forms; <c>null</c>
    /// when it is none of them.
    /// </summary>
    private static Result<ScpiScriptDirective?, ScpiScriptError>? TryParseKeyword(
        string text,
        int line
    )
    {
        if (text.StartsWith("sleep ", StringComparison.OrdinalIgnoreCase))
        {
            var arg = text["sleep ".Length..].Trim();
            if (
                !int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms)
                || ms < 0
            )
            {
                return Result.Failure<ScpiScriptDirective?, ScpiScriptError>(
                    new ScpiScriptInvalidDirective(
                        line,
                        text,
                        "sleep argument must be a non-negative integer"
                    )
                );
            }
            return Result.Success<ScpiScriptDirective?, ScpiScriptError>(
                new ScpiScriptDirective.Sleep(line, TimeSpan.FromMilliseconds(ms))
            );
        }
        if (text.StartsWith("assert ", StringComparison.OrdinalIgnoreCase))
        {
            var pattern = text["assert ".Length..].Trim();
            if (pattern.Length == 0)
            {
                return Result.Failure<ScpiScriptDirective?, ScpiScriptError>(
                    new ScpiScriptInvalidDirective(line, text, "assert requires a regex pattern")
                );
            }
            return Result.Success<ScpiScriptDirective?, ScpiScriptError>(
                new ScpiScriptDirective.Assert(line, pattern)
            );
        }
        if (text.StartsWith("echo ", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Success<ScpiScriptDirective?, ScpiScriptError>(
                new ScpiScriptDirective.Echo(line, text["echo ".Length..])
            );
        }
        return null;
    }
}

/// <summary>
/// One line of a script written in the form 0.4.0 removes, with what to
/// write instead. <see cref="Message"/> is the sentence a user reads.
/// </summary>
/// <param name="Line">1-based source line.</param>
/// <param name="Written">The line as written, trimmed.</param>
/// <param name="Replacement">What to write instead in the 0.4.0 form.</param>
/// <param name="Message">A one-line explanation; the caller adds the line number.</param>
public sealed record ScpiScriptDeprecation(
    int Line,
    string Written,
    string Replacement,
    string Message
)
{
    /// <summary>An unprefixed <c>sleep</c>, <c>assert</c> or <c>echo</c>.</summary>
    public static ScpiScriptDeprecation ForDirective(int line, string written) =>
        new(
            line,
            written,
            "!" + written,
            $"`{written}` is a directive only until 0.4.0, which sends it to the instrument as SCPI; write `!{written}`."
        );

    /// <summary>
    /// A <c>#</c> comment, on its own line or after SCPI text. From 0.4.0 a
    /// <c>#</c> belongs to the instrument: it starts block data and
    /// <c>#H</c>/<c>#Q</c>/<c>#B</c> numbers.
    /// </summary>
    public static ScpiScriptDeprecation ForComment(
        int line,
        string written,
        string beforeHash,
        string comment
    ) =>
        beforeHash.Length == 0
            ? new(line, written, "!" + comment, $"`#` comments end at 0.4.0; write `!{comment}`.")
            : new(
                line,
                written,
                "!" + comment,
                $"everything from `#` on is dropped here, but 0.4.0 sends it to the instrument (`#` also starts block data and #H/#Q/#B numbers); if `{comment}` is a comment, move it to its own `!{comment}` line."
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
