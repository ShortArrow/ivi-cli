using IviCli.Application.Audit;
using IviCli.Domain;
using IviCli.Domain.Mock;

namespace IviCli.Application.Mock;

/// <summary>Appends a new empty <see cref="MockScene"/> (state node) to a scenario.</summary>
public sealed class AddSceneCommandHandler
{
    private readonly IScenarioStore _store;
    private readonly IAuditLog _audit;
    private readonly IAuditSubject _subject;
    private readonly TimeProvider _time;

    /// <summary>Creates a new handler.</summary>
    public AddSceneCommandHandler(
        IScenarioStore store,
        IAuditLog? audit = null,
        IAuditSubject? subject = null,
        TimeProvider? time = null
    )
    {
        _store = store;
        _audit = audit ?? NullAuditLog.Instance;
        _subject = subject ?? new StaticAuditSubject("unknown");
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Validates inputs, loads the scenario, appends the empty scene, and saves.</summary>
    public async Task<Result<MockScenario, AddSceneError>> HandleAsync(
        AddSceneCommand command,
        CancellationToken ct
    )
    {
        if (
            ScenarioName.From(command.ScenarioName)
            is not Result<ScenarioName, ScenarioNameError>.Ok { Value: var name }
        )
        {
            return Result.Failure<MockScenario, AddSceneError>(
                new AddSceneInvalidScenarioName(command.ScenarioName)
            );
        }

        if (
            SceneName.From(command.SceneName)
            is not Result<SceneName, SceneNameError>.Ok { Value: var sceneName }
        )
        {
            return Result.Failure<MockScenario, AddSceneError>(
                new AddSceneInvalidSceneName(command.SceneName)
            );
        }

        var loadResult = await _store.LoadAsync(name, ct);
        if (loadResult is not Result<MockScenario, ScenarioStoreError>.Ok { Value: var scenario })
        {
            var err = ((Result<MockScenario, ScenarioStoreError>.Error)loadResult).Err;
            if (err is ScenarioNotFound)
            {
                return Result.Failure<MockScenario, AddSceneError>(
                    new AddSceneScenarioNotFound(name)
                );
            }
            return Result.Failure<MockScenario, AddSceneError>(new AddSceneStoreFailure(err));
        }

        if (scenario.FindScene(sceneName) is not null)
        {
            return Result.Failure<MockScenario, AddSceneError>(
                new AddSceneAlreadyExists(name, sceneName)
            );
        }

        var updated = scenario.AddScene(MockScene.Empty(sceneName));
        var saveResult = await _store.SaveAsync(updated, overwriteIfExists: true, ct);
        if (saveResult is not Result<Unit, ScenarioStoreError>.Ok)
        {
            var err = ((Result<Unit, ScenarioStoreError>.Error)saveResult).Err;
            return Result.Failure<MockScenario, AddSceneError>(new AddSceneStoreFailure(err));
        }

        await _audit.AppendAsync(
            new ConfigMutated(
                _time.GetUtcNow(),
                "scene.add",
                $"{name.Value}/{sceneName.Value}",
                _subject.Get()
            ),
            ct
        );

        return Result.Success<MockScenario, AddSceneError>(updated);
    }
}
