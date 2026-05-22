namespace IviCli.Domain;

/// <summary>
/// Errors that can arise when constructing a <see cref="Timeout"/>.
/// </summary>
public abstract record TimeoutError : IviError
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
/// The supplied duration could not be used as a <see cref="Timeout"/>
/// because it is negative or exceeds <see cref="Timeout.Maximum"/>.
/// </summary>
/// <param name="Raw">The raw duration provided by the caller.</param>
public sealed record InvalidTimeoutValue(TimeSpan Raw) : TimeoutError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid timeout value: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}
