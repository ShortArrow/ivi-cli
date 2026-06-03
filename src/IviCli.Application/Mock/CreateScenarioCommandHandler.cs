using IviCli.Application.Audit;
using IviCli.Domain;
using IviCli.Domain.Mock;

namespace IviCli.Application.Mock;

/// <summary>Creates a new (empty) scenario in the store.</summary>
public sealed class CreateScenarioCommandHandler
{
    private readonly IScenarioStore _store;
    private readonly IAuditLog _audit;
    private readonly IAuditSubject _subject;
    private readonly TimeProvider _time;

    /// <summary>Creates a new handler.</summary>
    public CreateScenarioCommandHandler(
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

        MockScenario scenario;
        if (command.InitialScene is { Length: > 0 } initialRaw)
        {
            if (
                SceneName.From(initialRaw)
                is not Result<SceneName, SceneNameError>.Ok { Value: var initialScene }
            )
            {
                return Result.Failure<ScenarioName, CreateScenarioError>(
                    new CreateScenarioInvalidInitialScene(initialRaw)
                );
            }
            scenario = new MockScenario(
                name,
                InitialScene: initialScene,
                IdnDefault: command.IdnDefault,
                Scenes: System.Collections.Immutable.ImmutableArray.Create(
                    MockScene.Empty(initialScene)
                )
            );
        }
        else
        {
            scenario = MockScenario.Empty(name) with { IdnDefault = command.IdnDefault };
        }
        var saveResult = await _store.SaveAsync(scenario, overwriteIfExists: false, ct);
        if (saveResult is Result<Unit, ScenarioStoreError>.Ok)
        {
            await _audit.AppendAsync(
                new ConfigMutated(_time.GetUtcNow(), "scenario.create", name.Value, _subject.Get()),
                ct
            );
            return Result.Success<ScenarioName, CreateScenarioError>(name);
        }
        return saveResult switch
        {
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
