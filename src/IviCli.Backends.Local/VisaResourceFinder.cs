using System.Collections.Immutable;
using IviCli.Domain;

namespace IviCli.Backends.Local;

/// <summary>
/// Production <see cref="IVisaResourceFinder"/> over the IVI Foundation
/// VISA.NET shared components. <c>Ivi.Visa.GlobalResourceManager</c> locates
/// an installed vendor implementation at runtime; every failure — no vendor
/// implementation registered, or the implementation reporting "no resources
/// found", which VISA surfaces as an exception — comes back as a
/// <see cref="LocalVisaError"/> rather than a throw.
/// </summary>
public sealed class VisaResourceFinder : IVisaResourceFinder
{
    /// <inheritdoc/>
    public Result<ImmutableArray<string>, LocalVisaError> Find(string pattern)
    {
        try
        {
            var found = Ivi.Visa.GlobalResourceManager.Find(pattern);
            return Result.Success<ImmutableArray<string>, LocalVisaError>(found.ToImmutableArray());
        }
        catch (Exception ex)
        {
            return Result.Failure<ImmutableArray<string>, LocalVisaError>(
                new LocalVisaIoFailure(
                    $"VISA resource enumeration for '{pattern}' failed: {ex.Message}",
                    ex
                )
            );
        }
    }
}
