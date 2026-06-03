using System.Collections.Immutable;
using IviCli.Application.Audit;
using IviCli.Domain;
using IviCli.Domain.Mock;

namespace IviCli.Application.Mock;

/// <summary>
/// Removes a <see cref="MockScene"/> (state node) from a scenario.
/// Refuses to remove the scenario's initial scene — the caller must
/// pick a new initial scene first (a follow-up feature) or remove the
/// whole scenario.
/// </summary>
public sealed class RemoveSceneCommandHandler
{
    private readonly IScenarioStore _store;
    private readonly IAuditLog _audit;
    private readonly IAuditSubject _subject;
    private readonly TimeProvider _time;

    /// <summary>Creates a new handler.</summary>
    public RemoveSceneCommandHandler(
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

    /// <summary>Validates inputs, loads the scenario, removes the scene, and saves.</summary>
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

        if (
            SceneName.From(command.SceneName)
            is not Result<SceneName, SceneNameError>.Ok { Value: var sceneName }
        )
        {
            return Result.Failure<MockScenario, RemoveSceneError>(
                new RemoveSceneInvalidSceneName(command.SceneName)
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

        if (scenario.FindScene(sceneName) is null)
        {
            return Result.Failure<MockScenario, RemoveSceneError>(
                new RemoveSceneNotFound(name, sceneName)
            );
        }

        if (scenario.InitialScene == sceneName)
        {
            return Result.Failure<MockScenario, RemoveSceneError>(
                new RemoveSceneIsInitial(name, sceneName)
            );
        }

        var trimmedScenes = scenario.Scenes.Where(s => s.Name != sceneName).ToImmutableArray();
        var updated = scenario with { Scenes = trimmedScenes };

        var saveResult = await _store.SaveAsync(updated, overwriteIfExists: true, ct);
        if (saveResult is not Result<Unit, ScenarioStoreError>.Ok)
        {
            var err = ((Result<Unit, ScenarioStoreError>.Error)saveResult).Err;
            return Result.Failure<MockScenario, RemoveSceneError>(new RemoveSceneStoreFailure(err));
        }

        await _audit.AppendAsync(
            new ConfigMutated(
                _time.GetUtcNow(),
                "scene.remove",
                $"{name.Value}/{sceneName.Value}",
                _subject.Get()
            ),
            ct
        );

        return Result.Success<MockScenario, RemoveSceneError>(updated);
    }
}
