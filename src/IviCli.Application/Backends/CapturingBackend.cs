using System.Diagnostics;
using IviCli.Application.Capture;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using Microsoft.Extensions.Logging;

namespace IviCli.Application.Backends;

/// <summary>
/// <see cref="IIviBackend"/> decorator that emits one
/// <see cref="TrafficEvent"/> per backend operation via the supplied
/// <see cref="ITrafficWriter"/>. The wrapped backend's results are
/// returned unchanged so verb behaviour is untouched; capture failures
/// are logged once at Warning and swallowed so the operator's traffic
/// never fails because the disk is full (ADR 0031).
/// </summary>
public sealed class CapturingBackend : IIviBackend
{
    private readonly IIviBackend _inner;
    private readonly ITrafficWriter _writer;
    private readonly ILogger<CapturingBackend>? _logger;

    /// <summary>Wraps <paramref name="inner"/> so each call is logged.</summary>
    public CapturingBackend(
        IIviBackend inner,
        ITrafficWriter writer,
        ILogger<CapturingBackend>? logger = null
    )
    {
        _inner = inner;
        _writer = writer;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<Unit, BackendError>> OpenAsync(Device device, CancellationToken ct)
    {
        var result = await _inner.OpenAsync(device, ct);
        await TryAppendAsync(
            BuildEvent(device, TrafficOp.Open, data: null, response: null, result, null),
            ct
        );
        return result;
    }

    /// <inheritdoc/>
    public async Task<Result<Unit, BackendError>> CloseAsync(Device device, CancellationToken ct)
    {
        var result = await _inner.CloseAsync(device, ct);
        await TryAppendAsync(
            BuildEvent(device, TrafficOp.Close, data: null, response: null, result, null),
            ct
        );
        return result;
    }

    /// <inheritdoc/>
    public async Task<Result<Unit, BackendError>> WriteAsync(
        Device device,
        ScpiCommand command,
        CancellationToken ct
    )
    {
        var result = await _inner.WriteAsync(device, command, ct);
        await TryAppendAsync(
            BuildEvent(device, TrafficOp.Write, data: command.Value, response: null, result, null),
            ct
        );
        return result;
    }

    /// <inheritdoc/>
    public async Task<Result<string, BackendError>> QueryAsync(
        Device device,
        ScpiQuery query,
        CancellationToken ct
    )
    {
        var sw = Stopwatch.StartNew();
        var result = await _inner.QueryAsync(device, query, ct);
        sw.Stop();
        var (response, error) = SplitResult(result);
        await TryAppendAsync(
            new TrafficEvent(
                DateTimeOffset.UtcNow,
                device.Name.Value,
                TrafficOp.Query,
                query.Value,
                response,
                Ok: error is null,
                LatencyMs: (int)sw.Elapsed.TotalMilliseconds,
                Error: error
            ),
            ct
        );
        return result;
    }

    /// <inheritdoc/>
    public async Task<Result<string, BackendError>> ReadAsync(Device device, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var result = await _inner.ReadAsync(device, ct);
        sw.Stop();
        var (response, error) = SplitResult(result);
        await TryAppendAsync(
            new TrafficEvent(
                DateTimeOffset.UtcNow,
                device.Name.Value,
                TrafficOp.Read,
                Data: null,
                Response: response,
                Ok: error is null,
                LatencyMs: (int)sw.Elapsed.TotalMilliseconds,
                Error: error
            ),
            ct
        );
        return result;
    }

    private static TrafficEvent BuildEvent(
        Device device,
        TrafficOp op,
        string? data,
        string? response,
        Result<Unit, BackendError> result,
        int? latencyMs
    )
    {
        var error = result is Result<Unit, BackendError>.Error err ? err.Err.Message : null;
        return new TrafficEvent(
            DateTimeOffset.UtcNow,
            device.Name.Value,
            op,
            data,
            response,
            Ok: error is null,
            LatencyMs: latencyMs,
            Error: error
        );
    }

    private static (string? Response, string? Error) SplitResult(
        Result<string, BackendError> result
    ) =>
        result switch
        {
            Result<string, BackendError>.Ok ok => (ok.Value, null),
            Result<string, BackendError>.Error err => (null, err.Err.Message),
            _ => (null, null),
        };

    private async Task TryAppendAsync(TrafficEvent ev, CancellationToken ct)
    {
        try
        {
            await _writer.AppendAsync(ev, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "VISA traffic capture sink failed for {Op} on {Device}; capture silently disabled for this event",
                ev.Op,
                ev.Device
            );
        }
    }
}
