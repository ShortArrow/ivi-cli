using System.Collections.Immutable;
using IviCli.Application.Capture;
using IviCli.Domain;
using IviCli.Domain.Devices;

namespace IviCli.Application.Mock;

/// <summary>
/// Query DTO for <c>mock writes</c> (requirement 2). Selects the SCPI writes
/// a device received, read back from an NDJSON traffic capture (ADR 0031) that
/// a serving gateway produced — so a separate process can confirm out-of-band
/// that a client's write (e.g. <c>:VOLT 24.000</c>) actually reached the mock.
/// </summary>
/// <param name="Device">Device alias whose writes to select.</param>
/// <param name="Match">
/// Optional substring the SCPI must contain (e.g. <c>:VOLT</c>); <see langword="null"/>
/// selects every write for the device.
/// </param>
/// <param name="Path">Filesystem path to the NDJSON capture.</param>
public sealed record MockWritesQuery(string Device, string? Match, string Path);

/// <summary>
/// Reads the NDJSON capture via <see cref="INdjsonTrafficReader"/> and returns
/// the matching <see cref="TrafficOp.Write"/> events for a device, in capture
/// order (oldest first, so the last element is the most recent write). Rendering
/// "last vs all" is left to the CLI.
/// </summary>
public sealed class MockWritesQueryHandler
{
    private readonly INdjsonTrafficReader _reader;

    /// <summary>Creates a handler over the supplied capture reader.</summary>
    public MockWritesQueryHandler(INdjsonTrafficReader reader)
    {
        _reader = reader;
    }

    /// <summary>Streams the capture and returns the device's matching writes.</summary>
    public async Task<Result<ImmutableArray<TrafficEvent>, MockWritesError>> HandleAsync(
        MockWritesQuery query,
        CancellationToken ct
    )
    {
        if (
            DeviceName.From(query.Device)
            is not Result<DeviceName, DeviceError>.Ok { Value: var device }
        )
        {
            return Result.Failure<ImmutableArray<TrafficEvent>, MockWritesError>(
                new MockWritesInvalidDevice(query.Device)
            );
        }

        try
        {
            var matches = ImmutableArray.CreateBuilder<TrafficEvent>();
            await foreach (var ev in _reader.ReadAsync(query.Path, ct))
            {
                if (ev.Op != TrafficOp.Write || ev.Device != device.Value || ev.Data is null)
                {
                    continue;
                }
                if (
                    query.Match is { } needle
                    && !ev.Data.Contains(needle, StringComparison.Ordinal)
                )
                {
                    continue;
                }
                matches.Add(ev);
            }
            return Result.Success<ImmutableArray<TrafficEvent>, MockWritesError>(
                matches.ToImmutable()
            );
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
            return Result.Failure<ImmutableArray<TrafficEvent>, MockWritesError>(
                new MockWritesIoFailure(query.Path, ex.Message, ex)
            );
        }
    }
}

/// <summary>Outcomes the <c>mock writes</c> query can fail with.</summary>
public abstract record MockWritesError : IviError
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

/// <summary>Supplied device alias does not parse.</summary>
public sealed record MockWritesInvalidDevice(string Raw) : MockWritesError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "invalid device alias: {Raw}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Raw };
}

/// <summary>The capture file could not be read.</summary>
public sealed record MockWritesIoFailure(string Path, string Reason, Exception? Inner = null)
    : MockWritesError
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
