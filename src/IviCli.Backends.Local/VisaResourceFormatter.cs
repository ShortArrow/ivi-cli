using IviCli.Domain.Visa;

namespace IviCli.Backends.Local;

/// <summary>Formats <see cref="VisaResource"/> back to a VISA resource string.</summary>
public static class VisaResourceFormatter
{
    /// <summary>Formats <paramref name="resource"/> to its canonical VISA resource string form.</summary>
    public static string Format(VisaResource resource) => resource.ToCanonical();
}
