namespace IviCli.Domain.Visa;

/// <summary>
/// Errors that can arise when working with VISA resource strings.
/// </summary>
public abstract record VisaResourceError;

/// <summary>
/// The supplied input could not be parsed into a recognised VISA resource.
/// </summary>
/// <param name="Raw">The raw input string as provided by the caller.</param>
public sealed record InvalidVisaResourceFormat(string Raw) : VisaResourceError;
