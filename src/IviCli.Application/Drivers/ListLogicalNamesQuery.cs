using System.Collections.Immutable;
using IviCli.Domain;
using IviCli.Domain.Drivers;

namespace IviCli.Application.Drivers;

/// <summary>Query DTO for <c>ivicli logical list</c> (PRD §6.5).</summary>
public sealed record ListLogicalNamesQuery;

/// <summary>The aggregated logical-name listing result.</summary>
public sealed record LogicalNameListing(ImmutableArray<IviLogicalName> LogicalNames);

/// <summary>
/// Application-layer handler that delegates to the configured
/// <see cref="IIviConfigurationStore"/>.
/// </summary>
public sealed class ListLogicalNamesQueryHandler
{
    private readonly IIviConfigurationStore _store;

    /// <summary>Creates a new handler bound to the supplied store.</summary>
    public ListLogicalNamesQueryHandler(IIviConfigurationStore store)
    {
        _store = store;
    }

    /// <summary>Returns the current logical-name listing.</summary>
    public async Task<Result<LogicalNameListing, IviConfigurationStoreError>> HandleAsync(
        ListLogicalNamesQuery query,
        CancellationToken ct
    )
    {
        var result = await _store.ListLogicalNamesAsync(ct);
        return result switch
        {
            Result<ImmutableArray<IviLogicalName>, IviConfigurationStoreError>.Ok ok =>
                Result.Success<LogicalNameListing, IviConfigurationStoreError>(
                    new LogicalNameListing(ok.Value)
                ),
            Result<ImmutableArray<IviLogicalName>, IviConfigurationStoreError>.Error err =>
                Result.Failure<LogicalNameListing, IviConfigurationStoreError>(err.Err),
            _ => throw new InvalidOperationException("unknown Result variant"),
        };
    }
}
