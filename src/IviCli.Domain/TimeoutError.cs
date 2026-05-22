namespace IviCli.Domain;

/// <summary>
/// Errors that can arise when constructing a <see cref="Timeout"/>.
/// </summary>
public abstract record TimeoutError;

/// <summary>
/// The supplied duration could not be used as a <see cref="Timeout"/>
/// because it is negative or exceeds <see cref="Timeout.Maximum"/>.
/// </summary>
/// <param name="Raw">The raw duration provided by the caller.</param>
public sealed record InvalidTimeoutValue(TimeSpan Raw) : TimeoutError;
