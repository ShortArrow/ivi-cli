using System.Collections.Immutable;
using IviCli.Domain;
using IviCli.Domain.Drivers;

namespace IviCli.Application.Drivers;

/// <summary>Query DTO for <c>ivicli driver list</c> (PRD §6.5).</summary>
public sealed record ListDriversQuery;

/// <summary>The aggregated driver-listing result.</summary>
public sealed record DriverListing(ImmutableArray<IviDriver> Drivers);

/// <summary>
/// Application-layer handler that delegates to the configured
/// <see cref="IIviConfigurationStore"/>. The handler is intentionally
/// trivial — the parsing and IO live in Infrastructure.
/// </summary>
public sealed class ListDriversQueryHandler
{
    private readonly IIviConfigurationStore _store;

    /// <summary>Creates a new handler bound to the supplied store.</summary>
    public ListDriversQueryHandler(IIviConfigurationStore store)
    {
        _store = store;
    }

    /// <summary>Returns the current driver listing.</summary>
    public async Task<Result<DriverListing, IviConfigurationStoreError>> HandleAsync(
        ListDriversQuery query,
        CancellationToken ct
    )
    {
        var result = await _store.ListDriversAsync(ct);
        return result switch
        {
            Result<ImmutableArray<IviDriver>, IviConfigurationStoreError>.Ok ok => Result.Success<
                DriverListing,
                IviConfigurationStoreError
            >(new DriverListing(ok.Value)),
            Result<ImmutableArray<IviDriver>, IviConfigurationStoreError>.Error err =>
                Result.Failure<DriverListing, IviConfigurationStoreError>(err.Err),
            _ => throw new InvalidOperationException("unknown Result variant"),
        };
    }
}
