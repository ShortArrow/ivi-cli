using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;
using IviCli.Domain.Session;

namespace IviCli.Application.Mock;

/// <summary>
/// Activates a scenario by binding it to a specific device in
/// <see cref="SessionState"/>. When no device is supplied, the
/// session's current device is used; calls fail when neither is set.
/// </summary>
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

    /// <summary>Activates the scenario for the resolved target device.</summary>
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

        var deviceResolution = ResolveDevice(command.Device, session);
        if (
            deviceResolution
            is not Result<DeviceName, ActivateScenarioError>.Ok { Value: var device }
        )
        {
            return Result.Failure<ScenarioName, ActivateScenarioError>(
                ((Result<DeviceName, ActivateScenarioError>.Error)deviceResolution).Err
            );
        }

        var next = session.BindScenario(device, name);
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

    internal static Result<DeviceName, ActivateScenarioError> ResolveDevice(
        string? raw,
        SessionState session
    )
    {
        if (raw is { Length: > 0 } explicitName)
        {
            return
                DeviceName.From(explicitName)
                    is Result<DeviceName, DeviceError>.Ok { Value: var parsed }
                ? Result.Success<DeviceName, ActivateScenarioError>(parsed)
                : Result.Failure<DeviceName, ActivateScenarioError>(
                    new ActivateScenarioInvalidDevice(explicitName)
                );
        }
        return session.CurrentDevice is { } current
            ? Result.Success<DeviceName, ActivateScenarioError>(current)
            : Result.Failure<DeviceName, ActivateScenarioError>(
                new ActivateScenarioNoDeviceSelected()
            );
    }
}

/// <summary>
/// Clears the scenario binding for the resolved target device. When no
/// binding existed, the call is a successful no-op.
/// </summary>
public sealed class DeactivateScenarioCommandHandler
{
    private readonly ISessionStore _sessionStore;

    /// <summary>Creates a new handler.</summary>
    public DeactivateScenarioCommandHandler(ISessionStore sessionStore)
    {
        _sessionStore = sessionStore;
    }

    /// <summary>Clears the active scenario binding for the resolved device.</summary>
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

        var deviceResolution = ActivateScenarioCommandHandler.ResolveDevice(
            command.Device,
            session
        );
        if (
            deviceResolution
            is not Result<DeviceName, ActivateScenarioError>.Ok { Value: var device }
        )
        {
            return Result.Failure<Unit, ActivateScenarioError>(
                ((Result<DeviceName, ActivateScenarioError>.Error)deviceResolution).Err
            );
        }

        var next = session.UnbindScenario(device);
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
