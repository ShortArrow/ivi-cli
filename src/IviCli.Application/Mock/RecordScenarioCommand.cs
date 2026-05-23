using IviCli.Application.Backends;
using IviCli.Application.Configuration;
using IviCli.Application.Scripting;
using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;
using IviCli.Domain.Scpi;
using IviCli.Domain.Session;

namespace IviCli.Application.Mock;

/// <summary>
/// Command DTO for <c>mock scenario record</c>. Runs the supplied script
/// source against the resolved device and appends an <see cref="MockScene"/>
/// per observed query/write into <paramref name="ScenarioName"/>.
/// </summary>
public sealed record RecordScenarioCommand(
    string ScenarioName,
    string? DeviceName,
    string ScriptSource
);

/// <summary>Errors that can surface from <see cref="RecordScenarioCommandHandler"/>.</summary>
public abstract record RecordScenarioError : IviError
{
    /// <inheritdoc/>
    public abstract LogSeverity Severity { get; }

    /// <inheritdoc/>
    public abstract string Message { get; }

    /// <inheritdoc/>
    public virtual IReadOnlyList<object?> LogArgs => Array.Empty<object?>();

    /// <inheritdoc/>
    public virtual Exception? Cause => null;
}

/// <summary>The scenario name failed validation.</summary>
public sealed record RecordScenarioInvalidName(string Raw) : RecordScenarioError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid scenario name: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>Script parse failure.</summary>
public sealed record RecordScenarioParseFailure(ScpiScriptError Inner) : RecordScenarioError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => Inner.Severity;

    /// <inheritdoc/>
    public override string Message => Inner.Message;

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => Inner.LogArgs;
}

/// <summary>Device validation failure.</summary>
public sealed record RecordScenarioInvalidDeviceName(string Raw) : RecordScenarioError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid device name: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>No device specified and no default known.</summary>
public sealed record RecordScenarioNoTarget : RecordScenarioError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "no device specified and no session/default set";
}

/// <summary>Device is not in the config.</summary>
public sealed record RecordScenarioUnknownDevice(DeviceName Name) : RecordScenarioError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "no such device: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>Inner store / backend failure.</summary>
public sealed record RecordScenarioStoreFailure(IviError Inner) : RecordScenarioError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => Inner.Severity;

    /// <inheritdoc/>
    public override string Message => Inner.Message;

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => Inner.LogArgs;
}

/// <summary>Backend transport failure during recording.</summary>
public sealed record RecordScenarioTransportFailure(BackendError Inner) : RecordScenarioError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => Inner.Severity;

    /// <inheritdoc/>
    public override string Message => Inner.Message;

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => Inner.LogArgs;

    /// <inheritdoc/>
    public override Exception? Cause => Inner.Cause;
}

/// <summary>The shape of a successful recording.</summary>
public sealed record RecordScenarioReport(ScenarioName ScenarioName, int ScenesRecorded);

/// <summary>
/// Executes a SCPI script against the resolved Backend while appending a
/// <see cref="MockScene"/> to <see cref="RecordScenarioCommand.ScenarioName"/>
/// per observed query (with the response) and write (acknowledged). Per
/// ADR 0027 §4, write traffic is recorded as <c>Ack</c> scenes and queries
/// as <c>Respond</c> scenes; assert / sleep / echo directives execute
/// without being recorded.
/// </summary>
public sealed class RecordScenarioCommandHandler
{
    private readonly IConfigStore _configStore;
    private readonly ISessionStore _sessionStore;
    private readonly IBackendFactory _backendFactory;
    private readonly IScenarioStore _scenarioStore;

    /// <summary>Creates a handler bound to the supplied stores and factory.</summary>
    public RecordScenarioCommandHandler(
        IConfigStore configStore,
        ISessionStore sessionStore,
        IBackendFactory backendFactory,
        IScenarioStore scenarioStore
    )
    {
        _configStore = configStore;
        _sessionStore = sessionStore;
        _backendFactory = backendFactory;
        _scenarioStore = scenarioStore;
    }

    /// <summary>Runs the recording and returns the produced scenario summary.</summary>
    public async Task<Result<RecordScenarioReport, RecordScenarioError>> HandleAsync(
        RecordScenarioCommand command,
        CancellationToken ct
    )
    {
        if (
            ScenarioName.From(command.ScenarioName)
            is not Result<ScenarioName, ScenarioNameError>.Ok { Value: var scenarioName }
        )
        {
            return Result.Failure<RecordScenarioReport, RecordScenarioError>(
                new RecordScenarioInvalidName(command.ScenarioName)
            );
        }

        var parseResult = ScpiScript.Parse(command.ScriptSource);
        if (parseResult is not Result<ScpiScript, ScpiScriptError>.Ok { Value: var script })
        {
            return Result.Failure<RecordScenarioReport, RecordScenarioError>(
                new RecordScenarioParseFailure(
                    ((Result<ScpiScript, ScpiScriptError>.Error)parseResult).Err
                )
            );
        }

        var configResult = await _configStore.LoadAsync(ct);
        if (configResult is not Result<ConfigDocument, ConfigStoreError>.Ok { Value: var config })
        {
            return Result.Failure<RecordScenarioReport, RecordScenarioError>(
                new RecordScenarioStoreFailure(
                    ((Result<ConfigDocument, ConfigStoreError>.Error)configResult).Err
                )
            );
        }

        DeviceName? targetName;
        if (command.DeviceName is { } rawName)
        {
            if (
                DeviceName.From(rawName)
                is not Result<DeviceName, DeviceError>.Ok { Value: var parsed }
            )
            {
                return Result.Failure<RecordScenarioReport, RecordScenarioError>(
                    new RecordScenarioInvalidDeviceName(rawName)
                );
            }
            targetName = parsed;
        }
        else
        {
            var sessionResult = await _sessionStore.LoadAsync(ct);
            if (
                sessionResult
                is not Result<SessionState, SessionStoreError>.Ok { Value: var session }
            )
            {
                return Result.Failure<RecordScenarioReport, RecordScenarioError>(
                    new RecordScenarioStoreFailure(
                        ((Result<SessionState, SessionStoreError>.Error)sessionResult).Err
                    )
                );
            }
            targetName = session.CurrentDevice ?? config.Defaults.Device;
            if (targetName is null)
            {
                return Result.Failure<RecordScenarioReport, RecordScenarioError>(
                    new RecordScenarioNoTarget()
                );
            }
        }

        var device = config.FindDevice(targetName);
        if (device is null)
        {
            return Result.Failure<RecordScenarioReport, RecordScenarioError>(
                new RecordScenarioUnknownDevice(targetName)
            );
        }

        var backendResult = _backendFactory.CreateFor(device);
        if (backendResult is not Result<IIviBackend, BackendError>.Ok { Value: var backend })
        {
            return Result.Failure<RecordScenarioReport, RecordScenarioError>(
                new RecordScenarioTransportFailure(
                    ((Result<IIviBackend, BackendError>.Error)backendResult).Err
                )
            );
        }

        var openResult = await backend.OpenAsync(device, ct);
        if (openResult is not Result<Unit, BackendError>.Ok)
        {
            return Result.Failure<RecordScenarioReport, RecordScenarioError>(
                new RecordScenarioTransportFailure(
                    ((Result<Unit, BackendError>.Error)openResult).Err
                )
            );
        }

        var scenesRecorded = 0;
        try
        {
            foreach (var directive in script.Directives)
            {
                ct.ThrowIfCancellationRequested();
                switch (directive)
                {
                    case ScpiScriptDirective.Write w:
                    {
                        if (
                            ScpiCommand.From(w.Text) is Result<ScpiCommand, ScpiError>.Ok
                            {
                                Value: var cmd
                            }
                        )
                        {
                            var wr = await backend.WriteAsync(device, cmd, ct);
                            if (wr is Result<Unit, BackendError>.Error werr)
                            {
                                return Result.Failure<RecordScenarioReport, RecordScenarioError>(
                                    new RecordScenarioTransportFailure(werr.Err)
                                );
                            }
                            var append = await _scenarioStore.AppendSceneAsync(
                                scenarioName,
                                new MockScene(w.Text, new SceneAction.Ack()),
                                ct
                            );
                            if (append is Result<MockScenario, ScenarioStoreError>.Error aerr)
                            {
                                return Result.Failure<RecordScenarioReport, RecordScenarioError>(
                                    new RecordScenarioStoreFailure(aerr.Err)
                                );
                            }
                            scenesRecorded++;
                        }
                        break;
                    }
                    case ScpiScriptDirective.Query q:
                    {
                        if (
                            ScpiQuery.From(q.Text) is Result<ScpiQuery, ScpiError>.Ok
                            {
                                Value: var query
                            }
                        )
                        {
                            var resp = await backend.QueryAsync(device, query, ct);
                            if (
                                resp
                                is not Result<string, BackendError>.Ok { Value: var responseText }
                            )
                            {
                                return Result.Failure<RecordScenarioReport, RecordScenarioError>(
                                    new RecordScenarioTransportFailure(
                                        ((Result<string, BackendError>.Error)resp).Err
                                    )
                                );
                            }
                            var append = await _scenarioStore.AppendSceneAsync(
                                scenarioName,
                                new MockScene(q.Text, new SceneAction.Respond(responseText)),
                                ct
                            );
                            if (append is Result<MockScenario, ScenarioStoreError>.Error aerr)
                            {
                                return Result.Failure<RecordScenarioReport, RecordScenarioError>(
                                    new RecordScenarioStoreFailure(aerr.Err)
                                );
                            }
                            scenesRecorded++;
                        }
                        break;
                    }
                    case ScpiScriptDirective.Sleep s:
                        await Task.Delay(s.Duration, ct);
                        break;
                    default:
                        // assert / echo are passive during recording.
                        break;
                }
            }
            return Result.Success<RecordScenarioReport, RecordScenarioError>(
                new RecordScenarioReport(scenarioName, scenesRecorded)
            );
        }
        finally
        {
            _ = await backend.CloseAsync(device, ct);
        }
    }
}
