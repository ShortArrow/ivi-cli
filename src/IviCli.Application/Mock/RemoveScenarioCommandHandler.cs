using IviCli.Domain;
using IviCli.Domain.Mock;

namespace IviCli.Application.Mock;

/// <summary>Removes a scenario from the store.</summary>
public sealed class RemoveScenarioCommandHandler
{
    private readonly IScenarioStore _store;

    /// <summary>Creates a new handler.</summary>
    public RemoveScenarioCommandHandler(IScenarioStore store)
    {
        _store = store;
    }

    /// <summary>Executes the removal.</summary>
    public async Task<Result<ScenarioName, RemoveScenarioError>> HandleAsync(
        RemoveScenarioCommand command,
        CancellationToken ct
    )
    {
        if (
            ScenarioName.From(command.Name)
            is not Result<ScenarioName, ScenarioNameError>.Ok { Value: var name }
        )
        {
            return Result.Failure<ScenarioName, RemoveScenarioError>(
                new RemoveScenarioInvalidName(command.Name)
            );
        }

        var deleteResult = await _store.DeleteAsync(name, ct);
        return deleteResult switch
        {
            Result<Unit, ScenarioStoreError>.Ok => Result.Success<
                ScenarioName,
                RemoveScenarioError
            >(name),
            Result<Unit, ScenarioStoreError>.Error { Err: ScenarioNotFound } => Result.Failure<
                ScenarioName,
                RemoveScenarioError
            >(new RemoveScenarioNotFound(name)),
            Result<Unit, ScenarioStoreError>.Error err => Result.Failure<
                ScenarioName,
                RemoveScenarioError
            >(new RemoveScenarioStoreFailure(err.Err)),
            _ => throw new InvalidOperationException("unknown Result variant"),
        };
    }
}
