namespace IviCli.Domain.Scpi;

/// <summary>
/// A SCPI query intended to be sent to an instrument and produce a textual
/// response. Construct via <see cref="From(string)"/>; the value must be a
/// request that <see cref="ScpiMessage.ExpectsResponse(string)"/> accepts.
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
        if (!ScpiMessage.ExpectsResponse(raw))
        {
            return Result.Failure<ScpiQuery, ScpiError>(
                new InvalidScpiQuery(raw, "no '?' ends a header or the text")
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

    /// <inheritdoc/>
    public override string ToString() => Value;
}
