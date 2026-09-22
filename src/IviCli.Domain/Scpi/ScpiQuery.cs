namespace IviCli.Domain.Scpi;

/// <summary>
/// A SCPI query intended to be sent to an instrument and produce a textual
/// response. Construct via <see cref="From(string)"/>; the value must be a
/// query as <see cref="IsQuery(string)"/> defines it.
/// </summary>
public sealed record ScpiQuery
{
    /// <summary>Inclusive upper bound on a SCPI query's length, in characters.</summary>
    public const int MaxLength = 4096;

    /// <summary>The raw SCPI text, as it will be transmitted to the instrument.</summary>
    public string Value { get; }

    private ScpiQuery(string value) => Value = value;

    /// <summary>Validates and constructs a <see cref="ScpiQuery"/>.</summary>
    public static Result<ScpiQuery, ScpiError> From(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return Result.Failure<ScpiQuery, ScpiError>(new InvalidScpiQuery(raw, "empty"));
        }
        if (raw.Length > MaxLength)
        {
            return Result.Failure<ScpiQuery, ScpiError>(
                new InvalidScpiQuery(raw, $"exceeds {MaxLength} characters")
            );
        }
        if (!IsQuery(raw))
        {
            return Result.Failure<ScpiQuery, ScpiError>(
                new InvalidScpiQuery(raw, "no program header ends with '?'")
            );
        }
        foreach (var c in raw)
        {
            if (char.IsControl(c) && c != '\r' && c != '\n' && c != '\t')
            {
                return Result.Failure<ScpiQuery, ScpiError>(
                    new InvalidScpiQuery(raw, "contains control characters")
                );
            }
        }
        return Result.Success<ScpiQuery, ScpiError>(new ScpiQuery(raw));
    }

    /// <summary>
    /// Whether <paramref name="text"/> expects a response: the header of at
    /// least one program message unit ends in <c>?</c> (IEEE 488.2). A header
    /// runs to the first whitespace, so parameters may follow it. Units are
    /// separated by <c>;</c> outside quoted strings and block data; a
    /// definite-length block (<c>#&lt;n&gt;&lt;length&gt;</c>) is skipped by
    /// its declared length and <c>#0</c> runs to the end of the text.
    /// </summary>
    public static bool IsQuery(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }
            var headerStart = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != ';')
            {
                i++;
            }
            if (i > headerStart && text[i - 1] == '?')
            {
                return true;
            }
            i = NextUnitStart(text, i);
        }
        return false;
    }

    private static int NextUnitStart(string text, int i)
    {
        while (i < text.Length)
        {
            switch (text[i])
            {
                case ';':
                    return i + 1;
                case '"' or '\'':
                    i = AfterQuotedString(text, i);
                    break;
                case '#':
                    i = AfterBlock(text, i);
                    break;
                default:
                    i++;
                    break;
            }
        }
        return i;
    }

    private static int AfterQuotedString(string text, int open)
    {
        var close = text.IndexOf(text[open], open + 1);
        return close < 0 ? text.Length : close + 1;
    }

    private static int AfterBlock(string text, int hash)
    {
        if (hash + 1 >= text.Length || !char.IsAsciiDigit(text[hash + 1]))
        {
            return hash + 1;
        }
        var digits = text[hash + 1] - '0';
        if (digits == 0)
        {
            return text.Length;
        }
        var lengthStart = hash + 2;
        if (lengthStart + digits > text.Length)
        {
            return text.Length;
        }
        if (!int.TryParse(text.AsSpan(lengthStart, digits), out var length))
        {
            return hash + 1;
        }
        var dataStart = lengthStart + digits;
        return (int)Math.Min(text.Length, (long)dataStart + length);
    }

    /// <inheritdoc/>
    public override string ToString() => Value;
}
