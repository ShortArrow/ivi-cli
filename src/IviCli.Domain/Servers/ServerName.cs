using System.Text.RegularExpressions;

namespace IviCli.Domain.Servers;

/// <summary>
/// A short, human-readable alias that identifies a gateway server
/// (for example <c>local</c>, <c>lab</c>).
/// </summary>
public sealed partial record ServerName
{
    /// <summary>The maximum permitted length, in characters.</summary>
    public const int MaxLength = 64;

    [GeneratedRegex("^[a-z][a-z0-9_-]*$")]
    private static partial Regex AllowedPattern();

    /// <summary>The underlying canonical string value.</summary>
    public string Value { get; }

    private ServerName(string value) => Value = value;

    /// <summary>Parses and validates a raw string into a <see cref="ServerName"/>.</summary>
    public static Result<ServerName, ServerNameError> From(string raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.Length > MaxLength || !AllowedPattern().IsMatch(raw))
        {
            return Result.Failure<ServerName, ServerNameError>(new InvalidServerNameFormat(raw));
        }
        return Result.Success<ServerName, ServerNameError>(new ServerName(raw));
    }

    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>Errors that can arise when constructing a <see cref="ServerName"/>.</summary>
public abstract record ServerNameError : IviError
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

/// <summary>The supplied input failed server-name format validation.</summary>
public sealed record InvalidServerNameFormat(string Raw) : ServerNameError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid server name format: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}
