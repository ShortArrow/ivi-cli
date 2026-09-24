namespace IviCli.Domain.Scpi;

/// <summary>
/// Reads the structure of a SCPI program message (IEEE 488.2 §7): its
/// program message units, their headers, and the quoted strings and block
/// data whose contents are not structure.
/// </summary>
public static class ScpiMessage
{
    /// <summary>
    /// Whether <paramref name="text"/> expects a response on the 0.3.x line:
    /// a header ends in <c>?</c> (<see cref="IsQuery(string)"/>), or the text
    /// ends in <c>?</c> as 0.3.1 decided. The first answers a query with
    /// parameters; the second keeps every request 0.3.1 answered answered.
    /// </summary>
    public static bool ExpectsResponse(string text) =>
        IsQuery(text) || text.TrimEnd().EndsWith('?');

    /// <summary>
    /// Whether <paramref name="text"/> expects a response: the header of at
    /// least one program message unit ends in <c>?</c> (IEEE 488.2). A header
    /// runs to the first whitespace, so parameters may follow it. Units are
    /// separated by <c>;</c>, and messages by a newline, outside quoted
    /// strings and block data; a definite-length block
    /// (<c>#&lt;n&gt;&lt;length&gt;</c>) is skipped by its declared length and
    /// <c>#0</c> runs to the end of the text.
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
                case ';' or '\n' or '\r':
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
        if (
            !int.TryParse(
                text.AsSpan(lengthStart, digits),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var length
            )
        )
        {
            return hash + 1;
        }
        var dataStart = lengthStart + digits;
        return (int)Math.Min(text.Length, (long)dataStart + length);
    }
}
