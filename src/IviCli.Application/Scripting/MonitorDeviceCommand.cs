using System.Globalization;
using IviCli.Application.Backends;
using IviCli.Application.Configuration;
using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Session;

namespace IviCli.Application.Scripting;

/// <summary>
/// Command DTO for <c>visa monitor</c>: repeatedly send <see cref="Query"/>
/// to the resolved device, separated by <see cref="Interval"/>, until
/// cancellation or <see cref="Count"/> samples have been produced.
/// </summary>
public sealed record MonitorDeviceCommand(
    string? Name,
    string Query,
    TimeSpan Interval,
    int? Count
);

/// <summary>
/// A single timestamped monitor sample. The handler emits these via the
/// supplied callback so the CLI layer controls formatting (plain text vs.
/// JSON per ADR 0027 §3).
/// </summary>
public sealed record MonitorSample(
    DateTimeOffset Timestamp,
    int Sequence,
    string Query,
    string Response
);

/// <summary>Errors that <see cref="MonitorDeviceCommandHandler"/> can return.</summary>
public abstract record MonitorDeviceError : IviError
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

/// <summary>The query text was not a valid SCPI query.</summary>
public sealed record MonitorDeviceInvalidQuery(string Raw) : MonitorDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid SCPI query: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>Interval was non-positive.</summary>
public sealed record MonitorDeviceInvalidInterval : MonitorDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "monitor interval must be positive";
}

/// <summary>Device name validation failed.</summary>
public sealed record MonitorDeviceInvalidName(string Raw) : MonitorDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid device name: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>No device was specified and none can be inferred.</summary>
public sealed record MonitorDeviceNoTarget : MonitorDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "no device specified and no session/default set";
}

/// <summary>The named device is not present in the config.</summary>
public sealed record MonitorDeviceUnknown(DeviceName Name) : MonitorDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "no such device: {Name}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Name };
}

/// <summary>Config or session store failure.</summary>
public sealed record MonitorDeviceStoreFailure(IviError Inner) : MonitorDeviceError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => Inner.Severity;

    /// <inheritdoc/>
    public override string Message => Inner.Message;

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => Inner.LogArgs;
}

/// <summary>Backend reported a transport failure mid-loop.</summary>
public sealed record MonitorDeviceTransportFailure(BackendError Inner) : MonitorDeviceError
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

/// <summary>
/// Polls the device with a SCPI query at a fixed interval, invoking a
/// callback per sample. The loop exits cleanly when the cancellation token
/// is signalled or the optional sample count is reached.
/// </summary>
public sealed class MonitorDeviceCommandHandler
{
    private readonly IConfigStore _configStore;
    private readonly ISessionStore _sessionStore;
    private readonly IBackendFactory _backendFactory;
    private readonly TimeProvider _time;

    /// <summary>Creates a handler bound to the supplied stores, factory and clock.</summary>
    public MonitorDeviceCommandHandler(
        IConfigStore configStore,
        ISessionStore sessionStore,
        IBackendFactory backendFactory,
        TimeProvider? time = null
    )
    {
        _configStore = configStore;
        _sessionStore = sessionStore;
        _backendFactory = backendFactory;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Runs the monitor loop until cancellation or completion.</summary>
    public async Task<Result<int, MonitorDeviceError>> HandleAsync(
        MonitorDeviceCommand command,
        Func<MonitorSample, Task> sink,
        CancellationToken ct
    )
    {
        if (command.Interval <= TimeSpan.Zero)
        {
            return Result.Failure<int, MonitorDeviceError>(new MonitorDeviceInvalidInterval());
        }

        var queryResult = ScpiQuery.From(command.Query);
        if (queryResult is not Result<ScpiQuery, ScpiError>.Ok { Value: var query })
        {
            return Result.Failure<int, MonitorDeviceError>(
                new MonitorDeviceInvalidQuery(command.Query)
            );
        }

        var configResult = await _configStore.LoadAsync(ct);
        if (configResult is not Result<ConfigDocument, ConfigStoreError>.Ok { Value: var config })
        {
            return Result.Failure<int, MonitorDeviceError>(
                new MonitorDeviceStoreFailure(
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
                return Result.Failure<int, MonitorDeviceError>(
                    new MonitorDeviceInvalidName(rawName)
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
                return Result.Failure<int, MonitorDeviceError>(
                    new MonitorDeviceStoreFailure(
                        ((Result<SessionState, SessionStoreError>.Error)sessionResult).Err
                    )
                );
            }
            targetName = session.CurrentDevice ?? config.Defaults.Device;
            if (targetName is null)
            {
                return Result.Failure<int, MonitorDeviceError>(new MonitorDeviceNoTarget());
            }
        }

        var device = config.FindDevice(targetName);
        if (device is null)
        {
            return Result.Failure<int, MonitorDeviceError>(new MonitorDeviceUnknown(targetName));
        }

        var backendResult = _backendFactory.CreateFor(device);
        if (backendResult is not Result<IIviBackend, BackendError>.Ok { Value: var backend })
        {
            return Result.Failure<int, MonitorDeviceError>(
                new MonitorDeviceTransportFailure(
                    ((Result<IIviBackend, BackendError>.Error)backendResult).Err
                )
            );
        }

        var openResult = await backend.OpenAsync(device, ct);
        if (openResult is not Result<Unit, BackendError>.Ok)
        {
            return Result.Failure<int, MonitorDeviceError>(
                new MonitorDeviceTransportFailure(
                    ((Result<Unit, BackendError>.Error)openResult).Err
                )
            );
        }

        var emitted = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var resp = await backend.QueryAsync(device, query, ct);
                if (resp is not Result<string, BackendError>.Ok { Value: var responseText })
                {
                    return Result.Failure<int, MonitorDeviceError>(
                        new MonitorDeviceTransportFailure(
                            ((Result<string, BackendError>.Error)resp).Err
                        )
                    );
                }
                emitted++;
                await sink(
                    new MonitorSample(
                        Timestamp: _time.GetUtcNow(),
                        Sequence: emitted,
                        Query: command.Query,
                        Response: responseText
                    )
                );
                if (command.Count is { } limit && emitted >= limit)
                {
                    break;
                }
                try
                {
                    await Task.Delay(command.Interval, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            return Result.Success<int, MonitorDeviceError>(emitted);
        }
        finally
        {
            _ = await backend.CloseAsync(device, ct);
        }
    }
}
