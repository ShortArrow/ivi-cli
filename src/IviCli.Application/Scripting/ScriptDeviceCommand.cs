using System.Text.RegularExpressions;
using IviCli.Application.Backends;
using IviCli.Application.Configuration;
using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Session;

namespace IviCli.Application.Scripting;

/// <summary>Command DTO for <c>visa script</c>.</summary>
public sealed record ScriptDeviceCommand(string? Name, string Source);

/// <summary>
/// Errors that can surface from <see cref="ScriptDeviceCommandHandler"/>.
/// </summary>
public abstract record ScriptDeviceError : IviError
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

/// <summary>The script could not be parsed.</summary>
public sealed record ScriptDeviceParseFailure(ScpiScriptError Inner) : ScriptDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => Inner.Severity;

    /// <inheritdoc/>
    public override string Message => Inner.Message;

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => Inner.LogArgs;
}

/// <summary>The supplied device name did not parse.</summary>
public sealed record ScriptDeviceInvalidName(string Raw) : ScriptDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid device name: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>No device was specified and no default is set.</summary>
public sealed record ScriptDeviceNoTarget : ScriptDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "no device specified and no session/default set";
}

/// <summary>The named device is not in the configuration.</summary>
public sealed record ScriptDeviceUnknown(DeviceName Name) : ScriptDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "no such device: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>Config or session store failure.</summary>
public sealed record ScriptDeviceStoreFailure(IviError Inner) : ScriptDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => Inner.Severity;

    /// <inheritdoc/>
    public override string Message => Inner.Message;

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => Inner.LogArgs;
}

/// <summary>The Backend reported a transport failure.</summary>
public sealed record ScriptDeviceTransportFailure(int Line, BackendError Inner) : ScriptDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => Inner.Severity;

    /// <inheritdoc/>
    public override string Message => "script line {Line}: " + Inner.Message;

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs
    {
        get
        {
            var inner = Inner.LogArgs;
            var args = new object?[1 + inner.Count];
            args[0] = Line;
            for (var i = 0; i < inner.Count; i++)
            {
                args[i + 1] = inner[i];
            }
            return args;
        }
    }

    /// <inheritdoc/>
    public override Exception? Cause => Inner.Cause;
}

/// <summary>An <c>assert</c> directive did not match the last response.</summary>
public sealed record ScriptDeviceAssertFailure(int Line, string Pattern, string Actual)
    : ScriptDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message =>
        "script line {Line}: assert /{Pattern}/ did not match {Actual}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Line, Pattern, Actual };
}

/// <summary>A summary of executed lines and the response transcript.</summary>
public sealed record ScriptExecutionReport(int LinesExecuted, IReadOnlyList<string> Output);

/// <summary>
/// Executes a parsed SCPI script against the resolved device, surfacing
/// the directive stream (echo lines, query responses) for the CLI layer
/// to display per ADR 0027 §2.
/// </summary>
public sealed class ScriptDeviceCommandHandler
{
    private readonly IConfigStore _configStore;
    private readonly ISessionStore _sessionStore;
    private readonly IBackendFactory _backendFactory;

    /// <summary>Creates a handler bound to the supplied stores and factory.</summary>
    public ScriptDeviceCommandHandler(
        IConfigStore configStore,
        ISessionStore sessionStore,
        IBackendFactory backendFactory
    )
    {
        _configStore = configStore;
        _sessionStore = sessionStore;
        _backendFactory = backendFactory;
    }

    /// <summary>Parses the script, resolves the device, and runs the directives.</summary>
    public async Task<Result<ScriptExecutionReport, ScriptDeviceError>> HandleAsync(
        ScriptDeviceCommand command,
        CancellationToken ct
    )
    {
        var parseResult = ScpiScript.Parse(command.Source);
        if (parseResult is not Result<ScpiScript, ScpiScriptError>.Ok { Value: var script })
        {
            return Result.Failure<ScriptExecutionReport, ScriptDeviceError>(
                new ScriptDeviceParseFailure(
                    ((Result<ScpiScript, ScpiScriptError>.Error)parseResult).Err
                )
            );
        }

        var configResult = await _configStore.LoadAsync(ct);
        if (configResult is not Result<ConfigDocument, ConfigStoreError>.Ok { Value: var config })
        {
            return Result.Failure<ScriptExecutionReport, ScriptDeviceError>(
                new ScriptDeviceStoreFailure(
                    ((Result<ConfigDocument, ConfigStoreError>.Error)configResult).Err
                )
            );
        }

        DeviceName? targetName;
        if (command.Name is { } rawName)
        {
            if (
                DeviceName.From(rawName)
                is not Result<DeviceName, DeviceError>.Ok { Value: var parsed }
            )
            {
                return Result.Failure<ScriptExecutionReport, ScriptDeviceError>(
                    new ScriptDeviceInvalidName(rawName)
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
                return Result.Failure<ScriptExecutionReport, ScriptDeviceError>(
                    new ScriptDeviceStoreFailure(
                        ((Result<SessionState, SessionStoreError>.Error)sessionResult).Err
                    )
                );
            }
            targetName = session.CurrentDevice ?? config.Defaults.Device;
            if (targetName is null)
            {
                return Result.Failure<ScriptExecutionReport, ScriptDeviceError>(
                    new ScriptDeviceNoTarget()
                );
            }
        }

        var device = config.FindDevice(targetName);
        if (device is null)
        {
            return Result.Failure<ScriptExecutionReport, ScriptDeviceError>(
                new ScriptDeviceUnknown(targetName)
            );
        }

        var backendResult = _backendFactory.CreateFor(device);
        if (backendResult is not Result<IIviBackend, BackendError>.Ok { Value: var backend })
        {
            return Result.Failure<ScriptExecutionReport, ScriptDeviceError>(
                new ScriptDeviceTransportFailure(
                    0,
                    ((Result<IIviBackend, BackendError>.Error)backendResult).Err
                )
            );
        }

        var openResult = await backend.OpenAsync(device, ct);
        if (openResult is not Result<Unit, BackendError>.Ok)
        {
            return Result.Failure<ScriptExecutionReport, ScriptDeviceError>(
                new ScriptDeviceTransportFailure(
                    0,
                    ((Result<Unit, BackendError>.Error)openResult).Err
                )
            );
        }

        var output = new List<string>();
        string? lastResponse = null;
        var executed = 0;
        try
        {
            foreach (var directive in script.Directives)
            {
                ct.ThrowIfCancellationRequested();
                switch (directive)
                {
                    case ScpiScriptDirective.Write w:
                    {
                        var cmd = ScpiCommand.From(w.Text).ShouldBeOkOrTransport(w.Line);
                        if (cmd is Result<ScpiCommand, BackendError>.Error err)
                        {
                            return Result.Failure<ScriptExecutionReport, ScriptDeviceError>(
                                new ScriptDeviceTransportFailure(w.Line, err.Err)
                            );
                        }
                        var ok = (Result<ScpiCommand, BackendError>.Ok)cmd;
                        var wr = await backend.WriteAsync(device, ok.Value, ct);
                        if (wr is Result<Unit, BackendError>.Error werr)
                        {
                            return Result.Failure<ScriptExecutionReport, ScriptDeviceError>(
                                new ScriptDeviceTransportFailure(w.Line, werr.Err)
                            );
                        }
                        break;
                    }
                    case ScpiScriptDirective.Query q:
                    {
                        var parsed = ScpiQuery.From(q.Text);
                        if (parsed is not Result<ScpiQuery, ScpiError>.Ok { Value: var qOk })
                        {
                            return Result.Failure<ScriptExecutionReport, ScriptDeviceError>(
                                new ScriptDeviceTransportFailure(
                                    q.Line,
                                    new TransportDisconnected("invalid SCPI query")
                                )
                            );
                        }
                        var resp = await backend.QueryAsync(device, qOk, ct);
                        if (resp is not Result<string, BackendError>.Ok { Value: var responseText })
                        {
                            return Result.Failure<ScriptExecutionReport, ScriptDeviceError>(
                                new ScriptDeviceTransportFailure(
                                    q.Line,
                                    ((Result<string, BackendError>.Error)resp).Err
                                )
                            );
                        }
                        lastResponse = responseText;
                        output.Add(responseText);
                        break;
                    }
                    case ScpiScriptDirective.Sleep s:
                        await Task.Delay(s.Duration, ct);
                        break;
                    case ScpiScriptDirective.Assert a:
                        if (
                            lastResponse is null
                            || !Regex.IsMatch(
                                lastResponse,
                                a.Pattern,
                                RegexOptions.None,
                                TimeSpan.FromSeconds(1)
                            )
                        )
                        {
                            return Result.Failure<ScriptExecutionReport, ScriptDeviceError>(
                                new ScriptDeviceAssertFailure(
                                    a.Line,
                                    a.Pattern,
                                    lastResponse ?? "(no prior query)"
                                )
                            );
                        }
                        break;
                    case ScpiScriptDirective.Echo e:
                        output.Add(e.Text);
                        break;
                }
                executed++;
            }
            return Result.Success<ScriptExecutionReport, ScriptDeviceError>(
                new ScriptExecutionReport(executed, output)
            );
        }
        finally
        {
            _ = await backend.CloseAsync(device, ct);
        }
    }
}

file static class CommandParseExtensions
{
    public static Result<ScpiCommand, BackendError> ShouldBeOkOrTransport(
        this Result<ScpiCommand, ScpiError> result,
        int line
    ) =>
        result switch
        {
            Result<ScpiCommand, ScpiError>.Ok ok => Result.Success<ScpiCommand, BackendError>(
                ok.Value
            ),
            Result<ScpiCommand, ScpiError>.Error => Result.Failure<ScpiCommand, BackendError>(
                new TransportDisconnected($"invalid SCPI on line {line}")
            ),
            _ => throw new InvalidOperationException(),
        };
}
