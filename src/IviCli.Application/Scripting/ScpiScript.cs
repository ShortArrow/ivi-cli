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
    /// Parses the supplied script source into a structured
    /// <see cref="ScpiScript"/>. Blank lines and lines starting with
    /// <c>#</c> are ignored. Trailing <c># ...</c> comments are stripped.
    /// </summary>
    public static Result<ScpiScript, ScpiScriptError> Parse(string source)
    {
        var directives = ImmutableArray.CreateBuilder<ScpiScriptDirective>();
        var lines = source.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var raw = StripComment(lines[i]).Trim();
            if (raw.Length == 0)
            {
                continue;
            }

            var parseResult = ParseDirective(raw, lineNumber);
            if (parseResult is Result<ScpiScriptDirective, ScpiScriptError>.Ok { Value: var d })
            {
                directives.Add(d);
            }
            else
            {
                return Result.Failure<ScpiScript, ScpiScriptError>(
                    ((Result<ScpiScriptDirective, ScpiScriptError>.Error)parseResult).Err
                );
            }
        }
        return Result.Success<ScpiScript, ScpiScriptError>(
            new ScpiScript(directives.ToImmutable())
        );
    }

    private static string StripComment(string line)
    {
        // Honor `#` only when it follows whitespace OR is the first non-space
        // character. SCPI text rarely contains `#` so a literal split is OK
        // for the v1 contract.
        var idx = line.IndexOf('#');
        return idx < 0 ? line : line[..idx];
    }

    private static Result<ScpiScriptDirective, ScpiScriptError> ParseDirective(string raw, int line)
    {
        if (raw.StartsWith("sleep ", StringComparison.OrdinalIgnoreCase))
        {
            var arg = raw["sleep ".Length..].Trim();
            if (
                !int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms)
                || ms < 0
            )
            {
                return Result.Failure<ScpiScriptDirective, ScpiScriptError>(
                    new ScpiScriptInvalidDirective(
                        line,
                        raw,
                        "sleep argument must be a non-negative integer"
                    )
                );
            }
            return Result.Success<ScpiScriptDirective, ScpiScriptError>(
                new ScpiScriptDirective.Sleep(line, TimeSpan.FromMilliseconds(ms))
            );
        }
        if (raw.StartsWith("assert ", StringComparison.OrdinalIgnoreCase))
        {
            var pattern = raw["assert ".Length..].Trim();
            if (pattern.Length == 0)
            {
                return Result.Failure<ScpiScriptDirective, ScpiScriptError>(
                    new ScpiScriptInvalidDirective(line, raw, "assert requires a regex pattern")
                );
            }
            return Result.Success<ScpiScriptDirective, ScpiScriptError>(
                new ScpiScriptDirective.Assert(line, pattern)
            );
        }
        if (raw.StartsWith("echo ", StringComparison.OrdinalIgnoreCase))
        {
            var text = raw["echo ".Length..];
            return Result.Success<ScpiScriptDirective, ScpiScriptError>(
                new ScpiScriptDirective.Echo(line, text)
            );
        }
        if (raw.TrimEnd().EndsWith('?'))
        {
            return Result.Success<ScpiScriptDirective, ScpiScriptError>(
                new ScpiScriptDirective.Query(line, raw)
            );
        }
        return Result.Success<ScpiScriptDirective, ScpiScriptError>(
            new ScpiScriptDirective.Write(line, raw)
        );
    }
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
