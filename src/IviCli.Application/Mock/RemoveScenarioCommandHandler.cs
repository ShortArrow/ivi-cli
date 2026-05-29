using IviCli.Application.Audit;
using IviCli.Domain;
using IviCli.Domain.Mock;

namespace IviCli.Application.Mock;

/// <summary>Removes a scenario from the store.</summary>
public sealed class RemoveScenarioCommandHandler
{
    private readonly IScenarioStore _store;
    private readonly IAuditLog _audit;
    private readonly IAuditSubject _subject;
    private readonly TimeProvider _time;

    /// <summary>Creates a new handler.</summary>
    public RemoveScenarioCommandHandler(
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
        if (deleteResult is Result<Unit, ScenarioStoreError>.Ok)
        {
            await _audit.AppendAsync(
                new ConfigMutated(_time.GetUtcNow(), "scenario.remove", name.Value, _subject.Get()),
                ct
            );
            return Result.Success<ScenarioName, RemoveScenarioError>(name);
        }
        return deleteResult switch
        {
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
