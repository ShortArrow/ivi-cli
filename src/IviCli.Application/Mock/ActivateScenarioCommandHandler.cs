using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Mock;
using IviCli.Domain.Session;

namespace IviCli.Application.Mock;

/// <summary>Activates a scenario by writing its name into <see cref="SessionState"/>.</summary>
public sealed class ActivateScenarioCommandHandler
{
    private readonly IScenarioStore _scenarioStore;
    private readonly ISessionStore _sessionStore;

    /// <summary>Creates a new handler.</summary>
    public ActivateScenarioCommandHandler(IScenarioStore scenarioStore, ISessionStore sessionStore)
    {
        _scenarioStore = scenarioStore;
        _sessionStore = sessionStore;
    }

    /// <summary>Activates the scenario.</summary>
    public async Task<Result<ScenarioName, ActivateScenarioError>> HandleAsync(
        ActivateScenarioCommand command,
        CancellationToken ct
    )
    {
        if (
            ScenarioName.From(command.Name)
            is not Result<ScenarioName, ScenarioNameError>.Ok { Value: var name }
        )
        {
            return Result.Failure<ScenarioName, ActivateScenarioError>(
                new ActivateScenarioInvalidName(command.Name)
            );
        }

        var existsResult = await _scenarioStore.ExistsAsync(name, ct);
        if (existsResult is not Result<bool, ScenarioStoreError>.Ok { Value: var exists })
        {
            var err = ((Result<bool, ScenarioStoreError>.Error)existsResult).Err;
            return Result.Failure<ScenarioName, ActivateScenarioError>(
                new ActivateScenarioStoreFailure(err)
            );
        }
        if (!exists)
        {
            return Result.Failure<ScenarioName, ActivateScenarioError>(
                new ActivateScenarioNotFound(name)
            );
        }

        var sessionResult = await _sessionStore.LoadAsync(ct);
        if (sessionResult is not Result<SessionState, SessionStoreError>.Ok { Value: var session })
        {
            var err = ((Result<SessionState, SessionStoreError>.Error)sessionResult).Err;
            return Result.Failure<ScenarioName, ActivateScenarioError>(
                new ActivateScenarioSessionFailure(err)
            );
        }

        var next = session with { ActiveScenario = name };
        var saveResult = await _sessionStore.SaveAsync(next, ct);
        if (saveResult is not Result<Unit, SessionStoreError>.Ok)
        {
            var err = ((Result<Unit, SessionStoreError>.Error)saveResult).Err;
            return Result.Failure<ScenarioName, ActivateScenarioError>(
                new ActivateScenarioSessionFailure(err)
            );
        }

        return Result.Success<ScenarioName, ActivateScenarioError>(name);
    }
}

/// <summary>Clears any active scenario from the session.</summary>
public sealed class DeactivateScenarioCommandHandler
{
    private readonly ISessionStore _sessionStore;

    /// <summary>Creates a new handler.</summary>
    public DeactivateScenarioCommandHandler(ISessionStore sessionStore)
    {
        _sessionStore = sessionStore;
    }

    /// <summary>Clears the active scenario.</summary>
    public async Task<Result<Unit, ActivateScenarioError>> HandleAsync(
        DeactivateScenarioCommand command,
        CancellationToken ct
    )
    {
        var sessionResult = await _sessionStore.LoadAsync(ct);
        if (sessionResult is not Result<SessionState, SessionStoreError>.Ok { Value: var session })
        {
            var err = ((Result<SessionState, SessionStoreError>.Error)sessionResult).Err;
            return Result.Failure<Unit, ActivateScenarioError>(
                new ActivateScenarioSessionFailure(err)
            );
        }

        var next = session with { ActiveScenario = null };
        var saveResult = await _sessionStore.SaveAsync(next, ct);
        if (saveResult is not Result<Unit, SessionStoreError>.Ok)
        {
            var err = ((Result<Unit, SessionStoreError>.Error)saveResult).Err;
            return Result.Failure<Unit, ActivateScenarioError>(
                new ActivateScenarioSessionFailure(err)
            );
        }
        return Result.Success<Unit, ActivateScenarioError>(Unit.Value);
    }
}
