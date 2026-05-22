using IviCli.Domain;
using IviCli.Domain.Mock;

namespace IviCli.Application.Mock;

/// <summary>Removes a scene by 1-based index from an existing scenario.</summary>
public sealed class RemoveSceneCommandHandler
{
    private readonly IScenarioStore _store;

    /// <summary>Creates a new handler.</summary>
    public RemoveSceneCommandHandler(IScenarioStore store)
    {
        _store = store;
    }

    /// <summary>Removes the requested scene.</summary>
    public async Task<Result<MockScenario, RemoveSceneError>> HandleAsync(
        RemoveSceneCommand command,
        CancellationToken ct
    )
    {
        if (
            ScenarioName.From(command.ScenarioName)
            is not Result<ScenarioName, ScenarioNameError>.Ok { Value: var name }
        )
        {
            return Result.Failure<MockScenario, RemoveSceneError>(
                new RemoveSceneInvalidScenarioName(command.ScenarioName)
            );
        }

        var loadResult = await _store.LoadAsync(name, ct);
        if (loadResult is not Result<MockScenario, ScenarioStoreError>.Ok { Value: var scenario })
        {
            var err = ((Result<MockScenario, ScenarioStoreError>.Error)loadResult).Err;
            if (err is ScenarioNotFound)
            {
                return Result.Failure<MockScenario, RemoveSceneError>(
                    new RemoveSceneScenarioNotFound(name)
                );
            }
            return Result.Failure<MockScenario, RemoveSceneError>(new RemoveSceneStoreFailure(err));
        }

        var updated = scenario.RemoveSceneAt(command.Index);
        if (updated is null)
        {
            return Result.Failure<MockScenario, RemoveSceneError>(
                new RemoveSceneIndexOutOfRange(command.Index, scenario.Scenes.Length)
            );
        }

        var saveResult = await _store.SaveAsync(updated, overwriteIfExists: true, ct);
        if (saveResult is not Result<Unit, ScenarioStoreError>.Ok)
        {
            var err = ((Result<Unit, ScenarioStoreError>.Error)saveResult).Err;
            return Result.Failure<MockScenario, RemoveSceneError>(new RemoveSceneStoreFailure(err));
        }

        return Result.Success<MockScenario, RemoveSceneError>(updated);
    }
}
