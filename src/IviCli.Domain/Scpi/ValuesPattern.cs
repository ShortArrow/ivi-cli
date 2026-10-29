using System.Collections.Immutable;
using System.Globalization;

namespace IviCli.Domain.Scpi;

/// <summary>
/// A <c>!values</c> pattern (ADR 0051 §5): slots separated by <c>,</c> and
/// <c>;</c> as a response is (<see cref="ScpiMessage.SplitResponse"/>). A
/// slot is a literal, compared ordinally after trimming; <c>*</c>, matching
/// any one element; or <c>{predicate}</c>, reading the element as a number
/// (NR1, NR2 or NR3, invariant culture, finite) and testing it with
/// <c>&gt;</c>, <c>&gt;=</c>, <c>&lt;</c>, <c>&lt;=</c> or an inclusive
/// range <c>lo..hi</c>. A predicate may carry a name, <c>{temp:20..30}</c>,
/// which appears only in a failure. A response matches when it has as many
/// units, and each unit as many elements, as the pattern, and every element
/// satisfies its slot. Block data satisfies only <c>*</c>.
/// </summary>
public sealed class ValuesPattern
{
    private readonly ImmutableArray<ImmutableArray<Slot>> _units;

    private ValuesPattern(ImmutableArray<ImmutableArray<Slot>> units) => _units = units;

    /// <summary>Reads <paramref name="text"/> as a pattern.</summary>
    public static Result<ValuesPattern, InvalidValuesPattern> Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Invalid(text, "the pattern is empty");
        }
        var units = ImmutableArray.CreateBuilder<ImmutableArray<Slot>>();
        var index = 0;
        foreach (var unit in ScpiMessage.SplitResponse(text))
        {
            var slots = ImmutableArray.CreateBuilder<Slot>();
            foreach (var element in unit)
            {
                index++;
                var slot = Slot.Parse(element.Text, index, out var reason);
                if (slot is null)
                {
                    return Invalid(text, reason!);
                }
                slots.Add(slot);
            }
            units.Add(slots.ToImmutable());
        }
        return Result.Success<ValuesPattern, InvalidValuesPattern>(
            new ValuesPattern(units.ToImmutable())
        );
    }

    /// <summary>
    /// Why <paramref name="response"/> fails the pattern, naming the first
    /// slot that refuses it, or <see langword="null"/> when it matches.
    /// </summary>
    public string? Match(string response)
    {
        var actual = ScpiMessage.SplitResponse(response);
        if (actual.Length != _units.Length)
        {
            return $"the response has {Count(actual.Length, "unit")}, the pattern {_units.Length}";
        }
        for (var u = 0; u < _units.Length; u++)
        {
            if (actual[u].Length != _units[u].Length)
            {
                var which = _units.Length == 1 ? "the response" : $"unit {u + 1} of the response";
                return $"{which} has {Count(actual[u].Length, "slot")}, the pattern {_units[u].Length}";
            }
        }
        var index = 0;
        for (var u = 0; u < _units.Length; u++)
        {
            for (var e = 0; e < _units[u].Length; e++)
            {
                index++;
                if (_units[u][e].Refuse(actual[u][e], index) is { } reason)
                {
                    return reason;
                }
            }
        }
        return null;
    }

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    private static Result<ValuesPattern, InvalidValuesPattern> Invalid(
        string text,
        string reason
    ) =>
        Result.Failure<ValuesPattern, InvalidValuesPattern>(new InvalidValuesPattern(text, reason));

    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value);

    private abstract record Slot
    {
        public abstract string? Refuse(ResponseElement element, int index);

        public static Slot? Parse(string text, int index, out string? reason)
        {
            reason = null;
            if (text.Length == 0)
            {
                reason = $"slot {index} is empty";
                return null;
            }
            if (text == "*")
            {
                return new Any();
            }
            if (text[0] != '{')
            {
                return new Literal(text);
            }
            if (text[^1] != '}')
            {
                reason = $"slot {index}: {{ is not closed";
                return null;
            }
            var predicate = Predicate.Parse(text[1..^1], out var why);
            if (predicate is null)
            {
                reason = $"slot {index}: {why}";
            }
            return predicate;
        }
    }

    private sealed record Any : Slot
    {
        public override string? Refuse(ResponseElement element, int index) => null;
    }

    private sealed record Literal(string Text) : Slot
    {
        public override string? Refuse(ResponseElement element, int index) =>
            element.IsBlock ? $"slot {index} is block data, which only * matches"
            : element.Text == Text ? null
            : $"slot {index} is {element.Text}, not {Text}";
    }

    private sealed record Predicate(string? Name, Func<double, bool> Holds, string Expected) : Slot
    {
        private static readonly string[] Comparisons = [">=", "<=", ">", "<"];

        public override string? Refuse(ResponseElement element, int index)
        {
            var label = Name is null ? $"slot {index}" : $"slot {index} ({Name})";
            if (element.IsBlock)
            {
                return $"{label} is block data, which only * matches";
            }
            if (!TryNumber(element.Text, out var value))
            {
                return $"{label} is not a number: {element.Text}";
            }
            return Holds(value) ? null : $"{label} is {element.Text}, not {Expected}";
        }

        public static Predicate? Parse(string body, out string? reason)
        {
            reason = null;
            string? name = null;
            var colon = body.IndexOf(':', StringComparison.Ordinal);
            if (colon >= 0)
            {
                name = body[..colon].Trim();
                if (!IsName(name))
                {
                    reason = $"{name} is not a slot name";
                    return null;
                }
                body = body[(colon + 1)..];
            }
            body = body.Trim();
            if (body.StartsWith('='))
            {
                reason = "equality is not a predicate; write a range such as 3.27..3.28";
                return null;
            }
            foreach (var op in Comparisons)
            {
                if (body.StartsWith(op, StringComparison.Ordinal))
                {
                    var boundText = body[op.Length..].Trim();
                    if (!TryNumber(boundText, out var bound))
                    {
                        reason = $"{boundText} is not a number";
                        return null;
                    }
                    Func<double, bool> holds = op switch
                    {
                        ">=" => v => v >= bound,
                        "<=" => v => v <= bound,
                        ">" => v => v > bound,
                        _ => v => v < bound,
                    };
                    return new Predicate(name, holds, $"{op} {boundText}");
                }
            }
            var dots = body.IndexOf("..", StringComparison.Ordinal);
            if (dots < 0)
            {
                reason = "expected >, >=, <, <= or a range lo..hi";
                return null;
            }
            var loText = body[..dots].Trim();
            var hiText = body[(dots + 2)..].Trim();
            foreach (var end in new[] { loText, hiText })
            {
                if (!TryNumber(end, out _))
                {
                    reason = $"{end} is not a number";
                    return null;
                }
            }
            TryNumber(loText, out var lo);
            TryNumber(hiText, out var hi);
            if (lo > hi)
            {
                reason = $"the range {loText}..{hiText} is empty";
                return null;
            }
            return new Predicate(name, v => v >= lo && v <= hi, $"in {loText}..{hiText}");
        }

        private static bool IsName(string name) =>
            name.Length > 0
            && (char.IsAsciiLetter(name[0]) || name[0] == '_')
            && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    }
}

/// <summary>The text is not a <see cref="ValuesPattern"/>.</summary>
public sealed record InvalidValuesPattern(string Raw, string Reason) : ScpiError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid values pattern: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Reason };
}
