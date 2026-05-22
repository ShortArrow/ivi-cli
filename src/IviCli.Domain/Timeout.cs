namespace IviCli.Domain;

/// <summary>
/// A non-negative, bounded duration used for instrument I/O operations.
/// </summary>
/// <remarks>
/// Wraps <see cref="TimeSpan"/> so the domain rejects accidental negative or
/// outsized values (which can mean "hang forever" in some VISA backends).
/// Construct instances through <see cref="From(TimeSpan)"/> or
/// <see cref="FromMilliseconds(int)"/>.
/// </remarks>
public sealed record Timeout
{
    /// <summary>
    /// Inclusive upper bound on a valid <see cref="Timeout"/>. One hour is far
    /// beyond any reasonable instrument-control operation and guards against
    /// unit-mistake bugs (e.g. nanoseconds * 10^9 mistaken for milliseconds).
    /// </summary>
    public static readonly TimeSpan Maximum = TimeSpan.FromHours(1);

    /// <summary>The underlying duration.</summary>
    public TimeSpan Value { get; }

    /// <summary>The duration in whole milliseconds (rounded toward zero).</summary>
    public int Milliseconds => (int)Value.TotalMilliseconds;

    private Timeout(TimeSpan value) => Value = value;

    /// <summary>
    /// Validates and constructs a <see cref="Timeout"/> from a <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="value">The candidate duration.</param>
    /// <returns>
    /// <see cref="Result{T, TError}.Ok"/> on success; otherwise
    /// <see cref="Result{T, TError}.Error"/> wrapping <see cref="InvalidTimeoutValue"/>.
    /// </returns>
    public static Result<Timeout, TimeoutError> From(TimeSpan value)
    {
        if (value < TimeSpan.Zero || value > Maximum)
        {
            return Result.Failure<Timeout, TimeoutError>(new InvalidTimeoutValue(value));
        }
        return Result.Success<Timeout, TimeoutError>(new Timeout(value));
    }

    /// <summary>
    /// Validates and constructs a <see cref="Timeout"/> from an integer
    /// number of milliseconds.
    /// </summary>
    /// <param name="milliseconds">The candidate duration in milliseconds.</param>
    /// <returns>The constructed <see cref="Timeout"/> or an error.</returns>
    public static Result<Timeout, TimeoutError> FromMilliseconds(int milliseconds) =>
        From(TimeSpan.FromMilliseconds(milliseconds));

    /// <inheritdoc/>
    public override string ToString() => $"{Milliseconds}ms";
}
