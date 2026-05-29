using System.Collections.Immutable;
using IviCli.Application.Audit;
using IviCli.Application.Capture;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;

namespace IviCli.Application.Mock;

/// <summary>
/// Command DTO for <c>mock scenario import</c> (ADR 0033). Captures the
/// inputs the handler needs to convert an NDJSON capture into a stored
/// <see cref="MockScenario"/>.
/// </summary>
/// <param name="Path">Filesystem path to the NDJSON capture.</param>
/// <param name="Name">Desired scenario name.</param>
/// <param name="DeviceFilter">Optional device-alias filter when the capture contains multiple devices.</param>
/// <param name="Force">When <see langword="true"/>, overwrite an existing scenario with the same name.</param>
public sealed record ImportScenarioFromTrafficCommand(
    string Path,
    string Name,
    string? DeviceFilter,
    bool Force
);

/// <summary>Outcome of a successful import — for the CLI to surface.</summary>
/// <param name="Name">Stored scenario name.</param>
/// <param name="Device">Device alias covered by the scenes.</param>
/// <param name="Scenes">How many scenes the scenario now contains.</param>
public sealed record ImportSummary(ScenarioName Name, string Device, int Scenes);

/// <summary>
/// Application-layer handler for the <c>mock scenario import</c> verb.
/// Reads the NDJSON capture via <see cref="INdjsonTrafficReader"/>,
/// builds a scenario via <see cref="ITrafficScenarioConverter"/>, and
/// persists it via <see cref="IScenarioStore"/>.
/// </summary>
public sealed class ImportScenarioFromTrafficCommandHandler
{
    private readonly INdjsonTrafficReader _reader;
    private readonly ITrafficScenarioConverter _converter;
    private readonly IScenarioStore _store;
    private readonly IAuditLog _audit;
    private readonly IAuditSubject _subject;
    private readonly TimeProvider _time;

    /// <summary>Creates a new handler.</summary>
    public ImportScenarioFromTrafficCommandHandler(
        INdjsonTrafficReader reader,
        ITrafficScenarioConverter converter,
        IScenarioStore store,
        IAuditLog? audit = null,
        IAuditSubject? subject = null,
        TimeProvider? time = null
    )
    {
        _reader = reader;
        _converter = converter;
        _store = store;
        _audit = audit ?? NullAuditLog.Instance;
        _subject = subject ?? new StaticAuditSubject("unknown");
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Runs the import pipeline end to end.</summary>
    public async Task<Result<ImportSummary, ImportTrafficError>> HandleAsync(
        ImportScenarioFromTrafficCommand command,
        CancellationToken ct
    )
    {
        if (
            ScenarioName.From(command.Name)
            is not Result<ScenarioName, ScenarioNameError>.Ok { Value: var name }
        )
        {
            return Result.Failure<ImportSummary, ImportTrafficError>(
                new ImportTrafficInvalidName(command.Name)
            );
        }

        DeviceName? deviceFilter = null;
        if (!string.IsNullOrWhiteSpace(command.DeviceFilter))
        {
            if (
                DeviceName.From(command.DeviceFilter)
                is not Result<DeviceName, DeviceError>.Ok { Value: var dn }
            )
            {
                return Result.Failure<ImportSummary, ImportTrafficError>(
                    new ImportTrafficInvalidDevice(command.DeviceFilter)
                );
            }
            deviceFilter = dn;
        }

        ImmutableArray<TrafficEvent> events;
        try
        {
            var collected = new List<TrafficEvent>();
            await foreach (var ev in _reader.ReadAsync(command.Path, ct))
            {
                collected.Add(ev);
            }
            events = collected.ToImmutableArray();
        }
        catch (Exception ex)
            when (ex
                    is FileNotFoundException
                        or DirectoryNotFoundException
                        or UnauthorizedAccessException
                        or IOException
                        or InvalidDataException
            )
        {
            return Result.Failure<ImportSummary, ImportTrafficError>(
                new ImportTrafficIoFailure(command.Path, ex.Message, ex)
            );
        }

        var convertResult = _converter.Convert(events, name, deviceFilter);
        if (
            convertResult
            is not Result<MockScenario, ConvertTrafficError>.Ok { Value: var scenario }
        )
        {
            var inner = ((Result<MockScenario, ConvertTrafficError>.Error)convertResult).Err;
            return Result.Failure<ImportSummary, ImportTrafficError>(
                new ImportTrafficConvert(inner)
            );
        }

        var saveResult = await _store.SaveAsync(scenario, command.Force, ct);
        if (saveResult is Result<Unit, ScenarioStoreError>.Error storeErr)
        {
            return Result.Failure<ImportSummary, ImportTrafficError>(
                new ImportTrafficStoreFailure(storeErr.Err)
            );
        }

        await _audit.AppendAsync(
            new ConfigMutated(_time.GetUtcNow(), "scenario.import", name.Value, _subject.Get()),
            ct
        );

        var device =
            deviceFilter?.Value
            ?? events.First(e => e.Op is TrafficOp.Write or TrafficOp.Query && e.Ok).Device;
        return Result.Success<ImportSummary, ImportTrafficError>(
            new ImportSummary(name, device, scenario.Scenes.Length)
        );
    }
}

/// <summary>Outcomes the import handler can fail with.</summary>
public abstract record ImportTrafficError : IviError
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

/// <summary>Supplied scenario name does not parse.</summary>
public sealed record ImportTrafficInvalidName(string Raw) : ImportTrafficError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid scenario name: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>Supplied --device alias does not parse.</summary>
public sealed record ImportTrafficInvalidDevice(string Raw) : ImportTrafficError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid device alias: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The capture file could not be read.</summary>
public sealed record ImportTrafficIoFailure(string Path, string Reason, Exception? Inner = null)
    : ImportTrafficError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "capture file {Path} could not be read: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Path, Reason };

    /// <inheritdoc/>
    public override Exception? Cause => Inner;
}

/// <summary>Conversion produced an error.</summary>
public sealed record ImportTrafficConvert(ConvertTrafficError Inner) : ImportTrafficError
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

/// <summary>The scenario store rejected the save.</summary>
public sealed record ImportTrafficStoreFailure(ScenarioStoreError Inner) : ImportTrafficError
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
