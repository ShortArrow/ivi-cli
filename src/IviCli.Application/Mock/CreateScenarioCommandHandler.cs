using IviCli.Domain;
using IviCli.Domain.Mock;

namespace IviCli.Application.Mock;

/// <summary>Creates a new (empty) scenario in the store.</summary>
public sealed class CreateScenarioCommandHandler
{
    private readonly IScenarioStore _store;

    /// <summary>Creates a new handler.</summary>
    public CreateScenarioCommandHandler(IScenarioStore store)
    {
        _store = store;
    }

    /// <summary>Validates and persists the new scenario.</summary>
    public async Task<Result<ScenarioName, CreateScenarioError>> HandleAsync(
        CreateScenarioCommand command,
        CancellationToken ct
    )
    {
        if (
            ScenarioName.From(command.Name)
            is not Result<ScenarioName, ScenarioNameError>.Ok { Value: var name }
        )
        {
            return Result.Failure<ScenarioName, CreateScenarioError>(
                new CreateScenarioInvalidName(command.Name)
            );
        }

        var scenario = MockScenario.Empty(name) with { IdnDefault = command.IdnDefault };
        var saveResult = await _store.SaveAsync(scenario, overwriteIfExists: false, ct);
        return saveResult switch
        {
            Result<Unit, ScenarioStoreError>.Ok => Result.Success<
                ScenarioName,
                CreateScenarioError
            >(name),
            Result<Unit, ScenarioStoreError>.Error { Err: ScenarioAlreadyExists } => Result.Failure<
                ScenarioName,
                CreateScenarioError
            >(new CreateScenarioAlreadyExists(name)),
            Result<Unit, ScenarioStoreError>.Error err => Result.Failure<
                ScenarioName,
                CreateScenarioError
            >(new CreateScenarioStoreFailure(err.Err)),
            _ => throw new InvalidOperationException("unknown Result variant"),
        };
    }
}
