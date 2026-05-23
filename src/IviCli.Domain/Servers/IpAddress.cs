namespace IviCli.Domain.Servers;

/// <summary>
/// A bind / connect address (IPv4 / IPv6 literal or hostname). Phase 2 v1
/// accepts the syntactic form only — DNS resolution and IP-literal parsing
/// happen at socket open time.
/// </summary>
public sealed record IpAddress
{
    /// <summary>Loopback IPv4 address (PRD §7 / ADR 0007 §4 default bind).</summary>
    public static IpAddress Loopback { get; } = new("127.0.0.1");

    /// <summary>"Bind to every interface" address.</summary>
    public static IpAddress Any { get; } = new("0.0.0.0");

    /// <summary>The underlying canonical string value.</summary>
    public string Value { get; }

    private IpAddress(string value) => Value = value;

    /// <summary>Validates and constructs an <see cref="IpAddress"/>.</summary>
    public static Result<IpAddress, IpAddressError> From(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Result.Failure<IpAddress, IpAddressError>(new InvalidIpAddress(raw));
        }
        return Result.Success<IpAddress, IpAddressError>(new IpAddress(raw.Trim()));
    }

    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>Errors that can arise when constructing an <see cref="IpAddress"/>.</summary>
public abstract record IpAddressError : IviError
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

/// <summary>The supplied input failed IP / hostname validation.</summary>
public sealed record InvalidIpAddress(string Raw) : IpAddressError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid bind address: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}
