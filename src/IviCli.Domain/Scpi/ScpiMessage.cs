using System.Collections.Immutable;

namespace IviCli.Domain.Scpi;

/// <summary>
/// One element of a response message unit, trimmed. Block data is kept
/// whole and marked, because its contents are not structure.
/// </summary>
/// <param name="Text">The element's text.</param>
/// <param name="IsBlock">Whether the element is arbitrary block data.</param>
public readonly record struct ResponseElement(string Text, bool IsBlock);

/// <summary>
/// Reads the structure of a SCPI program message (IEEE 488.2 §7): its
/// program message units, their headers, and the quoted strings and block
/// data whose contents are not structure.
/// </summary>
public static class ScpiMessage
{
    /// <summary>
    /// <paramref name="text"/> without the whitespace and terminator after its
    /// last data. Whitespace before the terminator is not part of the message
    /// (IEEE 488.2 §7), but the contents of quoted strings and block data are:
    /// a definite-length block keeps its declared length even when it ends in
    /// whitespace, and an indefinite block (<c>#0</c>) loses only the newline
    /// that terminates it.
    /// </summary>
    public static string TrimEnd(string text)
    {
        var (dataEnd, indefinite) = LastData(text);
        if (indefinite)
        {
            return text.EndsWith('\n') ? text[..^1] : text;
        }
        var end = text.Length;
        while (end > dataEnd && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }
        return text[..end];
    }

    private static (int End, bool Indefinite) LastData(string text)
    {
        var end = 0;
        var indefinite = false;
        var i = 0;
        while (i < text.Length)
        {
            switch (text[i])
            {
                case '"' or '\'':
                    i = AfterQuotedString(text, i);
                    end = i;
                    indefinite = false;
                    break;
                case '#':
                    var after = AfterBlock(text, i);
                    if (after > i + 1)
                    {
                        end = after;
                        indefinite = text[i + 1] == '0';
                    }
                    i = after;
                    break;
                default:
                    i++;
                    break;
            }
        }
        return (end, indefinite);
    }

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

    /// <summary>
    /// <paramref name="text"/> with every character upper-cased except the
    /// contents of quoted strings and block data. Headers and character
    /// parameters are case-insensitive (IEEE 488.2 §7.6.1, SCPI 1999 §6.1),
    /// while string and block data are taken as sent.
    /// </summary>
    public static string FoldCase(string text)
    {
        var folded = new System.Text.StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var dataEnd = text[i] switch
            {
                '"' or '\'' => AfterQuotedString(text, i),
                '#' => AfterBlock(text, i),
                _ => i + 1,
            };
            if (dataEnd > i + 1)
            {
                folded.Append(text, i, dataEnd - i);
            }
            else
            {
                folded.Append(char.ToUpperInvariant(text[i]));
            }
            i = dataEnd;
        }
        return folded.ToString();
    }

    /// <summary>
    /// <paramref name="text"/> read as a response (ADR 0051 §3): message
    /// units separated by <c>;</c>, each a list of elements separated by
    /// <c>,</c>, outside quoted strings and block data. A response carries
    /// no header path, so nothing is resolved across units. An element that
    /// is block data is kept whole, separators included.
    /// </summary>
    public static ImmutableArray<ImmutableArray<ResponseElement>> SplitResponse(string text)
    {
        var units = ImmutableArray.CreateBuilder<ImmutableArray<ResponseElement>>();
        var elements = ImmutableArray.CreateBuilder<ResponseElement>();
        var start = 0;
        var isBlock = false;
        var i = 0;
        while (i < text.Length)
        {
            switch (text[i])
            {
                case '"' or '\'':
                    i = AfterQuotedString(text, i);
                    break;
                case '#':
                    var after = AfterBlock(text, i);
                    isBlock |= after > i + 1 && text.AsSpan(start, i - start).IsWhiteSpace();
                    i = after;
                    break;
                case ',' or ';':
                    elements.Add(Element(text, start, i, isBlock));
                    if (text[i] == ';')
                    {
                        units.Add(elements.ToImmutable());
                        elements.Clear();
                    }
                    start = i + 1;
                    isBlock = false;
                    i++;
                    break;
                default:
                    i++;
                    break;
            }
        }
        elements.Add(Element(text, start, text.Length, isBlock));
        units.Add(elements.ToImmutable());
        return units.ToImmutable();
    }

    private static ResponseElement Element(string text, int start, int end, bool isBlock) =>
        new(text[start..end].Trim(), isBlock);

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
