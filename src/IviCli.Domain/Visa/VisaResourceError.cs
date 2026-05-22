namespace IviCli.Domain.Visa;

/// <summary>
/// Errors that can arise when working with VISA resource strings.
/// </summary>
public abstract record VisaResourceError : IviError
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

/// <summary>
/// The supplied input could not be parsed into a recognised VISA resource.
/// </summary>
/// <param name="Raw">The raw input string as provided by the caller.</param>
public sealed record InvalidVisaResourceFormat(string Raw) : VisaResourceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid VISA resource format: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}
