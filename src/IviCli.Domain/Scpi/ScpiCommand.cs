namespace IviCli.Domain.Scpi;

/// <summary>
/// A SCPI command intended to be written to an instrument (no value
/// returned to the caller). Construct via <see cref="From(string)"/>.
/// </summary>
public sealed record ScpiCommand
{
    /// <summary>Inclusive upper bound on a SCPI command's length, in characters.</summary>
    public const int MaxLength = 4096;

    /// <summary>The raw SCPI text, as it will be transmitted to the instrument.</summary>
    public string Value { get; }

    private ScpiCommand(string value) => Value = value;

    /// <summary>Validates and constructs a <see cref="ScpiCommand"/>.</summary>
    public static Result<ScpiCommand, ScpiError> From(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return Result.Failure<ScpiCommand, ScpiError>(new InvalidScpiCommand(raw, "empty"));
        }
        if (raw.Length > MaxLength)
        {
            return Result.Failure<ScpiCommand, ScpiError>(
                new InvalidScpiCommand(raw, $"exceeds {MaxLength} characters")
            );
        }
        foreach (var c in raw)
        {
            // Reject control characters except CR / LF / HT, which are legal
            // in some multi-line SCPI bodies.
            if (char.IsControl(c) && c != '\r' && c != '\n' && c != '\t')
            {
                return Result.Failure<ScpiCommand, ScpiError>(
                    new InvalidScpiCommand(raw, "contains control characters")
                );
            }
        }
        return Result.Success<ScpiCommand, ScpiError>(new ScpiCommand(raw));
    }

    /// <inheritdoc/>
    public override string ToString() => Value;
}
