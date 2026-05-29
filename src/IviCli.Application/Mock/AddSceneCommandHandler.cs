using IviCli.Application.Audit;
using IviCli.Domain;
using IviCli.Domain.Mock;

namespace IviCli.Application.Mock;

/// <summary>Appends a scene to an existing scenario.</summary>
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

    /// <summary>Validates inputs, loads the scenario, appends the scene, and saves.</summary>
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
        if (string.IsNullOrEmpty(command.Match))
        {
            return Result.Failure<MockScenario, AddSceneError>(
                new AddSceneInvalidMatch(command.Match)
            );
        }

        // Resolve the action. Exactly one of (Respond, Ack, Fail) must be set.
        var actionCount =
            (command.Respond is not null ? 1 : 0)
            + (command.Ack ? 1 : 0)
            + (command.Fail is not null ? 1 : 0);
        if (actionCount != 1)
        {
            return Result.Failure<MockScenario, AddSceneError>(new AddSceneActionAmbiguous());
        }

        SceneAction action;
        if (command.Respond is not null)
        {
            action = new SceneAction.Respond(command.Respond);
        }
        else if (command.Ack)
        {
            action = new SceneAction.Ack();
        }
        else
        {
            action = new SceneAction.Fail(command.Fail!, command.FailDetail);
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

        var updated = scenario.AddScene(new MockScene(command.Match, action));
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
                $"{name.Value}/{command.Match}",
                _subject.Get()
            ),
            ct
        );

        return Result.Success<MockScenario, AddSceneError>(updated);
    }
}
