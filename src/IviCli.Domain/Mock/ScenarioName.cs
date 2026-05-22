using System.Text.RegularExpressions;

namespace IviCli.Domain.Mock;

/// <summary>
/// A short, file-name-safe alias that identifies a mock scenario
/// (for example <c>psu-startup</c>, <c>scope-noise</c>).
/// </summary>
public sealed partial record ScenarioName
{
    /// <summary>The maximum permitted length, in characters.</summary>
    public const int MaxLength = 64;

    [GeneratedRegex("^[a-z][a-z0-9_-]*$")]
    private static partial Regex AllowedPattern();

    /// <summary>The underlying canonical string value.</summary>
    public string Value { get; }

    private ScenarioName(string value) => Value = value;

    /// <summary>Parses and validates a raw string into a <see cref="ScenarioName"/>.</summary>
    public static Result<ScenarioName, ScenarioNameError> From(string raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.Length > MaxLength || !AllowedPattern().IsMatch(raw))
        {
            return Result.Failure<ScenarioName, ScenarioNameError>(
                new InvalidScenarioNameFormat(raw)
            );
        }
        return Result.Success<ScenarioName, ScenarioNameError>(new ScenarioName(raw));
    }

    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>Errors that can arise when constructing a <see cref="ScenarioName"/>.</summary>
public abstract record ScenarioNameError : IviError
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

/// <summary>The supplied input failed scenario-name format validation.</summary>
public sealed record InvalidScenarioNameFormat(string Raw) : ScenarioNameError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid scenario name format: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}
