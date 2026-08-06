using System.Collections.Immutable;
using IviCli.Domain;

namespace IviCli.Backends.Local;

/// <summary>
/// Port for enumerating the VISA resource strings a runtime can currently
/// see. Callers supply a VISA search pattern (e.g. <c>USB?*::INSTR</c>) and
/// receive the matching resource strings verbatim, unparsed. The production
/// implementation reflectively loads the IVI VISA shared component at
/// runtime; tests provide an in-memory fake.
/// </summary>
public interface IVisaResourceFinder
{
    /// <summary>
    /// Returns the resource strings matching <paramref name="pattern"/>, or a
    /// <see cref="LocalVisaError"/> when no VISA runtime is available or the
    /// enumeration failed.
    /// </summary>
    Result<ImmutableArray<string>, LocalVisaError> Find(string pattern);
}
