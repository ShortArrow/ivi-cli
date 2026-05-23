using System.Text.RegularExpressions;

namespace IviCli.Domain.Servers;

/// <summary>
/// The public endpoint name a client connects to (for HiSLIP this is
/// <c>hislip0</c>, <c>hislip1</c>, etc.; for SOCKET it is the port number
/// as a string, e.g. <c>5025</c>). Stored as a strongly-typed string so
/// routing logic does not see raw <c>string</c>s in the domain.
/// </summary>
public sealed partial record PublicEndpoint
{
    /// <summary>The maximum permitted length, in characters.</summary>
    public const int MaxLength = 32;

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]*$")]
    private static partial Regex AllowedPattern();

    /// <summary>The underlying canonical string value.</summary>
    public string Value { get; }

    private PublicEndpoint(string value) => Value = value;

    /// <summary>Validates and constructs a <see cref="PublicEndpoint"/>.</summary>
    public static Result<PublicEndpoint, PublicEndpointError> From(string raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.Length > MaxLength || !AllowedPattern().IsMatch(raw))
        {
            return Result.Failure<PublicEndpoint, PublicEndpointError>(
                new InvalidPublicEndpoint(raw)
            );
        }
        return Result.Success<PublicEndpoint, PublicEndpointError>(new PublicEndpoint(raw));
    }

    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>Errors that can arise when constructing a <see cref="PublicEndpoint"/>.</summary>
public abstract record PublicEndpointError : IviError
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

/// <summary>The supplied input failed public-endpoint format validation.</summary>
public sealed record InvalidPublicEndpoint(string Raw) : PublicEndpointError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid public endpoint: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}
