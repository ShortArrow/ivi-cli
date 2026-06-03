using System.Text.RegularExpressions;

namespace IviCli.Domain.Mock;

/// <summary>
/// A short, file-name-safe alias that identifies a <see cref="MockScene"/>
/// (state) inside a <see cref="MockScenario"/>. Same lexical rules as
/// <see cref="ScenarioName"/>: lowercase, starts with a letter, hyphen
/// and underscore allowed.
/// </summary>
public sealed partial record SceneName
{
    /// <summary>The maximum permitted length, in characters.</summary>
    public const int MaxLength = 64;

    /// <summary>The synthetic name used for v0.1.x scenarios that have
    /// no explicit scenes — every flat rule lives under this scene.</summary>
    public const string Default = "default";

    [GeneratedRegex("^[a-z][a-z0-9_-]*$")]
    private static partial Regex AllowedPattern();

    /// <summary>The underlying canonical string value.</summary>
    public string Value { get; }

    private SceneName(string value) => Value = value;

    /// <summary>Parses and validates a raw string into a <see cref="SceneName"/>.</summary>
    public static Result<SceneName, SceneNameError> From(string raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.Length > MaxLength || !AllowedPattern().IsMatch(raw))
        {
            return Result.Failure<SceneName, SceneNameError>(new InvalidSceneNameFormat(raw));
        }
        return Result.Success<SceneName, SceneNameError>(new SceneName(raw));
    }

    /// <summary>Returns the canonical <c>default</c> scene name (synthetic
    /// home for v0.1.x flat scenarios).</summary>
    public static SceneName DefaultScene() => new(Default);

    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>Errors that can arise when constructing a <see cref="SceneName"/>.</summary>
public abstract record SceneNameError : IviError
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

/// <summary>The supplied input failed scene-name format validation.</summary>
public sealed record InvalidSceneNameFormat(string Raw) : SceneNameError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid scene name format: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}
