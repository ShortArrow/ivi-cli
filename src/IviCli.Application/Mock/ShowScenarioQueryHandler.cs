using IviCli.Domain;
using IviCli.Domain.Mock;

namespace IviCli.Application.Mock;

/// <summary>Loads a scenario for display.</summary>
public sealed class ShowScenarioQueryHandler
{
    private readonly IScenarioStore _store;

    /// <summary>Creates a new handler.</summary>
    public ShowScenarioQueryHandler(IScenarioStore store)
    {
        _store = store;
    }

    /// <summary>Loads the scenario.</summary>
    public async Task<Result<MockScenario, ShowScenarioError>> HandleAsync(
        ShowScenarioQuery query,
        CancellationToken ct
    )
    {
        if (
            ScenarioName.From(query.Name)
            is not Result<ScenarioName, ScenarioNameError>.Ok { Value: var name }
        )
        {
            return Result.Failure<MockScenario, ShowScenarioError>(
                new ShowScenarioInvalidName(query.Name)
            );
        }

        var loadResult = await _store.LoadAsync(name, ct);
        return loadResult switch
        {
            Result<MockScenario, ScenarioStoreError>.Ok ok => Result.Success<
                MockScenario,
                ShowScenarioError
            >(ok.Value),
            Result<MockScenario, ScenarioStoreError>.Error { Err: ScenarioNotFound } =>
                Result.Failure<MockScenario, ShowScenarioError>(new ShowScenarioNotFound(name)),
            Result<MockScenario, ScenarioStoreError>.Error err => Result.Failure<
                MockScenario,
                ShowScenarioError
            >(new ShowScenarioStoreFailure(err.Err)),
            _ => throw new InvalidOperationException("unknown Result variant"),
        };
    }
}
