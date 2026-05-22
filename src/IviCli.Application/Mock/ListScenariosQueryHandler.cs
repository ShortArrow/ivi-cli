using System.Collections.Immutable;
using IviCli.Domain;
using IviCli.Domain.Mock;

namespace IviCli.Application.Mock;

/// <summary>Lists every scenario currently in the store.</summary>
public sealed class ListScenariosQueryHandler
{
    private readonly IScenarioStore _store;

    /// <summary>Creates a new handler.</summary>
    public ListScenariosQueryHandler(IScenarioStore store)
    {
        _store = store;
    }

    /// <summary>Executes the listing.</summary>
    public async Task<Result<ScenarioListing, ListScenariosError>> HandleAsync(
        ListScenariosQuery query,
        CancellationToken ct
    )
    {
        var result = await _store.ListAsync(ct);
        return result switch
        {
            Result<ImmutableArray<ScenarioName>, ScenarioStoreError>.Ok ok => Result.Success<
                ScenarioListing,
                ListScenariosError
            >(new ScenarioListing(ok.Value)),
            Result<ImmutableArray<ScenarioName>, ScenarioStoreError>.Error err => Result.Failure<
                ScenarioListing,
                ListScenariosError
            >(new ListScenariosStoreFailure(err.Err)),
            _ => throw new InvalidOperationException("unknown Result variant"),
        };
    }
}
