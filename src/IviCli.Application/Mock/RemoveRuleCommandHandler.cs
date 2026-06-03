using IviCli.Application.Audit;
using IviCli.Domain;
using IviCli.Domain.Mock;

namespace IviCli.Application.Mock;

/// <summary>Removes a <see cref="MockRule"/> from a scene inside a scenario by 1-based index.</summary>
public sealed class RemoveRuleCommandHandler
{
    private readonly IScenarioStore _store;
    private readonly IAuditLog _audit;
    private readonly IAuditSubject _subject;
    private readonly TimeProvider _time;

    /// <summary>Creates a new handler.</summary>
    public RemoveRuleCommandHandler(
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

    /// <summary>Validates inputs, loads the scenario, removes the rule, and saves.</summary>
    public async Task<Result<MockScenario, RemoveRuleError>> HandleAsync(
        RemoveRuleCommand command,
        CancellationToken ct
    )
    {
        if (
            ScenarioName.From(command.ScenarioName)
            is not Result<ScenarioName, ScenarioNameError>.Ok { Value: var name }
        )
        {
            return Result.Failure<MockScenario, RemoveRuleError>(
                new RemoveRuleInvalidScenarioName(command.ScenarioName)
            );
        }

        SceneName? requestedScene = null;
        if (command.Scene is { Length: > 0 } sceneRaw)
        {
            if (
                SceneName.From(sceneRaw)
                is not Result<SceneName, SceneNameError>.Ok { Value: var parsedScene }
            )
            {
                return Result.Failure<MockScenario, RemoveRuleError>(
                    new RemoveRuleInvalidSceneName(sceneRaw)
                );
            }
            requestedScene = parsedScene;
        }

        var loadResult = await _store.LoadAsync(name, ct);
        if (loadResult is not Result<MockScenario, ScenarioStoreError>.Ok { Value: var scenario })
        {
            var err = ((Result<MockScenario, ScenarioStoreError>.Error)loadResult).Err;
            if (err is ScenarioNotFound)
            {
                return Result.Failure<MockScenario, RemoveRuleError>(
                    new RemoveRuleScenarioNotFound(name)
                );
            }
            return Result.Failure<MockScenario, RemoveRuleError>(new RemoveRuleStoreFailure(err));
        }

        var targetSceneName = requestedScene ?? scenario.InitialScene;
        var targetScene = scenario.FindScene(targetSceneName);
        if (targetScene is null)
        {
            return Result.Failure<MockScenario, RemoveRuleError>(
                new RemoveRuleSceneNotFound(name, targetSceneName)
            );
        }

        var trimmed = targetScene.RemoveRuleAt(command.Index);
        if (trimmed is null)
        {
            return Result.Failure<MockScenario, RemoveRuleError>(
                new RemoveRuleIndexOutOfRange(command.Index, targetScene.Rules.Length)
            );
        }

        var updated = scenario.ReplaceScene(trimmed)!;
        var saveResult = await _store.SaveAsync(updated, overwriteIfExists: true, ct);
        if (saveResult is not Result<Unit, ScenarioStoreError>.Ok)
        {
            var err = ((Result<Unit, ScenarioStoreError>.Error)saveResult).Err;
            return Result.Failure<MockScenario, RemoveRuleError>(new RemoveRuleStoreFailure(err));
        }

        await _audit.AppendAsync(
            new ConfigMutated(
                _time.GetUtcNow(),
                "rule.remove",
                $"{name.Value}/{targetSceneName.Value}/{command.Index}",
                _subject.Get()
            ),
            ct
        );

        return Result.Success<MockScenario, RemoveRuleError>(updated);
    }
}
