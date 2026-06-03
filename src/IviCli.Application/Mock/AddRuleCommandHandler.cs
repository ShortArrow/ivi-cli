using IviCli.Application.Audit;
using IviCli.Domain;
using IviCli.Domain.Mock;

namespace IviCli.Application.Mock;

/// <summary>Appends a <see cref="MockRule"/> to a target scene inside a scenario.</summary>
public sealed class AddRuleCommandHandler
{
    private readonly IScenarioStore _store;
    private readonly IAuditLog _audit;
    private readonly IAuditSubject _subject;
    private readonly TimeProvider _time;

    /// <summary>Creates a new handler.</summary>
    public AddRuleCommandHandler(
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

    /// <summary>Validates inputs, loads the scenario, appends the rule, and saves.</summary>
    public async Task<Result<MockScenario, AddRuleError>> HandleAsync(
        AddRuleCommand command,
        CancellationToken ct
    )
    {
        if (
            ScenarioName.From(command.ScenarioName)
            is not Result<ScenarioName, ScenarioNameError>.Ok { Value: var name }
        )
        {
            return Result.Failure<MockScenario, AddRuleError>(
                new AddRuleInvalidScenarioName(command.ScenarioName)
            );
        }
        if (string.IsNullOrEmpty(command.Match))
        {
            return Result.Failure<MockScenario, AddRuleError>(
                new AddRuleInvalidMatch(command.Match)
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
                return Result.Failure<MockScenario, AddRuleError>(
                    new AddRuleInvalidSceneName(sceneRaw)
                );
            }
            requestedScene = parsedScene;
        }

        SceneName? transitionTo = null;
        if (command.TransitionTo is { Length: > 0 } transRaw)
        {
            if (
                SceneName.From(transRaw)
                is not Result<SceneName, SceneNameError>.Ok { Value: var parsedTrans }
            )
            {
                return Result.Failure<MockScenario, AddRuleError>(
                    new AddRuleInvalidSceneName(transRaw)
                );
            }
            transitionTo = parsedTrans;
        }

        var actionCount =
            (command.Respond is not null ? 1 : 0)
            + (command.Ack ? 1 : 0)
            + (command.Fail is not null ? 1 : 0);
        if (actionCount != 1)
        {
            return Result.Failure<MockScenario, AddRuleError>(new AddRuleActionAmbiguous());
        }

        RuleAction action;
        if (command.Respond is not null)
        {
            action = new RuleAction.Respond(command.Respond, transitionTo);
        }
        else if (command.Ack)
        {
            action = new RuleAction.Ack(transitionTo);
        }
        else
        {
            action = new RuleAction.Fail(command.Fail!, command.FailDetail, transitionTo);
        }

        var loadResult = await _store.LoadAsync(name, ct);
        if (loadResult is not Result<MockScenario, ScenarioStoreError>.Ok { Value: var scenario })
        {
            var err = ((Result<MockScenario, ScenarioStoreError>.Error)loadResult).Err;
            if (err is ScenarioNotFound)
            {
                return Result.Failure<MockScenario, AddRuleError>(
                    new AddRuleScenarioNotFound(name)
                );
            }
            return Result.Failure<MockScenario, AddRuleError>(new AddRuleStoreFailure(err));
        }

        var targetSceneName = requestedScene ?? scenario.InitialScene;
        var targetScene = scenario.FindScene(targetSceneName);
        if (targetScene is null)
        {
            return Result.Failure<MockScenario, AddRuleError>(
                new AddRuleSceneNotFound(name, targetSceneName)
            );
        }

        var updated = scenario.ReplaceScene(
            targetScene.AddRule(new MockRule(command.Match, action))
        )!;
        var saveResult = await _store.SaveAsync(updated, overwriteIfExists: true, ct);
        if (saveResult is not Result<Unit, ScenarioStoreError>.Ok)
        {
            var err = ((Result<Unit, ScenarioStoreError>.Error)saveResult).Err;
            return Result.Failure<MockScenario, AddRuleError>(new AddRuleStoreFailure(err));
        }

        await _audit.AppendAsync(
            new ConfigMutated(
                _time.GetUtcNow(),
                "rule.add",
                $"{name.Value}/{targetSceneName.Value}/{command.Match}",
                _subject.Get()
            ),
            ct
        );

        return Result.Success<MockScenario, AddRuleError>(updated);
    }
}
