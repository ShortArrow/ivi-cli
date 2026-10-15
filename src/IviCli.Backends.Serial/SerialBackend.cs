using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;

namespace IviCli.Backends.Serial;

/// <summary>
/// <see cref="IIviBackend"/> for an instrument on a serial port, opened by
/// name with no VISA runtime (ADR 0055 §2). It opens
/// <see cref="VisaResource.AsrlPort"/>; a board number is resolved to a port
/// by backend selection.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The port is opened with the device's serial settings, or VISA's
/// defaults, and with the device timeout as its read and write timeout, so
/// the port's own timeout ends a silent read first (ADR 0055 §8).</item>
/// <item>A command ends with the device's write terminator, LF unless set.
/// A reply ends at the read terminator; unset, it ends at LF and loses a
/// trailing CR, as a read through VISA does (ADR 0055 §5).</item>
/// <item>Blocking port calls run off the caller's thread, so the timeout
/// decorator can abandon one that does not return.</item>
/// <item>A serial line has no service requests: the stream ends at once.
/// Trigger writes <c>*TRG</c>.</item>
/// </list>
/// </remarks>
public sealed class SerialBackend : IIviBackend, IEnforcesDeviceTimeout
{
    private const string DefaultWriteTerminator = "\n";
    private const string DefaultReadTerminator = "\n";

    private readonly ISerialLineFactory _lines;
    private readonly ConcurrentDictionary<DeviceName, ISerialLine> _sessions = new();

    /// <summary>Opens ports through <paramref name="lines"/>.</summary>
    public SerialBackend(ISerialLineFactory lines) => _lines = lines;

    /// <inheritdoc/>
    public async Task<Result<Unit, BackendError>> OpenAsync(Device device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (device.Resource is not VisaResource.AsrlPort { Port: var port })
        {
            return Result.Failure<Unit, BackendError>(new UnsupportedTransport(device.Name));
        }

        ISerialLine line;
        try
        {
            line = await Task.Run(
                () =>
                    _lines.Open(
                        port,
                        device.Serial ?? SerialSettings.Default,
                        device.Timeout.Value
                    ),
                ct
            );
        }
        catch (UnauthorizedAccessException)
        {
            return Result.Failure<Unit, BackendError>(
                new TransportInUse(device.Name, device.Resource)
            );
        }
        catch (ArgumentException ex)
            when (ex.ParamName is { } setting
                && !setting.Equals("portName", StringComparison.OrdinalIgnoreCase)
            )
        {
            // The driver refused a setting, which the exception names
            // (ADR 0055 §4); its message is left out as it may carry the port.
            return Result.Failure<Unit, BackendError>(
                new TransportDisconnected(
                    $"opening {device.Resource.ToLogString()} failed: the serial driver refused {setting}"
                )
            );
        }
        catch (Exception ex)
            when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            return Result.Failure<Unit, BackendError>(
                new TransportDisconnected(Failure("opening", device, ex))
            );
        }

        if (_sessions.TryRemove(device.Name, out var previous))
        {
            previous.Dispose();
        }
        _sessions[device.Name] = line;
        return Result.Success<Unit, BackendError>(Unit.Value);
    }

    /// <inheritdoc/>
    public Task<Result<Unit, BackendError>> CloseAsync(Device device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_sessions.TryRemove(device.Name, out var line))
        {
            line.Dispose();
        }
        return Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));
    }

    /// <inheritdoc/>
    public Task<Result<Unit, BackendError>> WriteAsync(
        Device device,
        ScpiCommand command,
        CancellationToken ct
    ) => OnLine(device, line => Write(line, device, command.Value), ct);

    /// <inheritdoc/>
    public Task<Result<string, BackendError>> QueryAsync(
        Device device,
        ScpiQuery query,
        CancellationToken ct
    ) =>
        OnLine(
            device,
            line =>
            {
                Write(line, device, query.Value);
                return Read(line, device);
            },
            ct
        );

    /// <inheritdoc/>
    public Task<Result<string, BackendError>> ReadAsync(Device device, CancellationToken ct) =>
        OnLine(device, line => Read(line, device), ct);

    /// <inheritdoc/>
    public Task<Result<Unit, BackendError>> TriggerAsync(Device device, CancellationToken ct) =>
        OnLine(device, line => Write(line, device, "*TRG"), ct);

    /// <inheritdoc/>
#pragma warning disable CS1998
    public async IAsyncEnumerable<ServiceRequest> ServiceRequestStream(
        Device device,
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        yield break;
    }
#pragma warning restore CS1998

    private static Unit Write(ISerialLine line, Device device, string text)
    {
        line.Write(text + (device.WriteTermination?.Value ?? DefaultWriteTerminator));
        return Unit.Value;
    }

    private static string Read(ISerialLine line, Device device) =>
        device.ReadTermination is { } terminator
            ? line.ReadTo(terminator.Value)
            : line.ReadTo(DefaultReadTerminator).TrimEnd('\r');

    /// <summary>
    /// Runs <paramref name="operation"/> on the device's open line, off the
    /// caller's thread, and maps the line's failures to backend errors.
    /// </summary>
    private async Task<Result<T, BackendError>> OnLine<T>(
        Device device,
        Func<ISerialLine, T> operation,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        if (!_sessions.TryGetValue(device.Name, out var line))
        {
            return Result.Failure<T, BackendError>(
                new TransportDisconnected("serial session not open")
            );
        }
        try
        {
            return Result.Success<T, BackendError>(await Task.Run(() => operation(line), ct));
        }
        catch (TimeoutException)
        {
            return Result.Failure<T, BackendError>(new TransportTimeout(device.Timeout.Value));
        }
        catch (Exception ex)
            when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return Result.Failure<T, BackendError>(
                new TransportDisconnected(Failure("using", device, ex))
            );
        }
    }

    /// <summary>
    /// A failure reason that names the resource masked and the exception by
    /// type and code: a port exception's message, like the exception itself,
    /// can carry the port's full name, which logs mask (ADR 0055 §1).
    /// </summary>
    private static string Failure(string action, Device device, Exception ex) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{action} {device.Resource.ToLogString()} failed ({ex.GetType().Name}, 0x{ex.HResult:X8})"
        );
}
