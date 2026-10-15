using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using IviCli.Application.Backends;
using IviCli.Application.Configuration;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;

namespace IviCli.Infrastructure.Backends;

/// <summary>
/// <see cref="IIviBackend"/> for an <c>ASRL&lt;board&gt;::INSTR</c> device
/// (ADR 0055 §2, §3). At each open it reads the machine's <c>[[asrl]]</c>
/// table: a registered board opens its port on the serial backend, and an
/// unregistered one opens through VISA, which decides what the board is.
/// Later operations go to the backend that opened the device.
/// </summary>
/// <remarks>
/// The table is read when a session opens rather than when ivi-cli starts,
/// so a running gateway sees a board registered after it started from the
/// next open; a session already open keeps the backend it opened on.
/// </remarks>
public sealed class AsrlBoardBackend : IIviBackend, IEnforcesDeviceTimeout
{
    private readonly IConfigStore _store;
    private readonly IIviBackend _serial;
    private readonly IIviBackend? _visa;
    private readonly ConcurrentDictionary<DeviceName, IIviBackend> _openedOn = new();

    /// <summary>
    /// Opens registered boards on <paramref name="serial"/> and unregistered
    /// ones on <paramref name="visa"/>, when a VISA backend is available.
    /// </summary>
    public AsrlBoardBackend(IConfigStore store, IIviBackend serial, IIviBackend? visa)
    {
        _store = store;
        _serial = serial;
        _visa = visa;
    }

    /// <inheritdoc/>
    public async Task<Result<Unit, BackendError>> OpenAsync(Device device, CancellationToken ct)
    {
        if (device.Resource is not VisaResource.AsrlBoard { Board: var board })
        {
            return Result.Failure<Unit, BackendError>(new UnsupportedTransport(device.Name));
        }
        if (await _store.LoadAsync(ct) is not Result<ConfigDocument, ConfigStoreError>.Ok config)
        {
            return Result.Failure<Unit, BackendError>(
                new TransportDisconnected("the [[asrl]] board table could not be read")
            );
        }

        if (config.Value.FindAsrlBoard(board) is { Port: var port })
        {
            return Opened(
                device,
                _serial,
                await _serial.OpenAsync(
                    device with
                    {
                        Resource = new VisaResource.AsrlPort(port),
                    },
                    ct
                )
            );
        }
        if (_visa is null)
        {
            return Result.Failure<Unit, BackendError>(
                new AsrlBoardUnavailable(device.Name, board, VisaError: null)
            );
        }
        var throughVisa = await _visa.OpenAsync(device, ct);
        return throughVisa is Result<Unit, BackendError>.Error { Err: var visaError }
            ? Result.Failure<Unit, BackendError>(
                new AsrlBoardUnavailable(device.Name, board, visaError)
            )
            : Opened(device, _visa, throughVisa);
    }

    /// <inheritdoc/>
    public Task<Result<Unit, BackendError>> CloseAsync(Device device, CancellationToken ct) =>
        _openedOn.TryRemove(device.Name, out var backend)
            ? backend.CloseAsync(device, ct)
            : Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

    /// <inheritdoc/>
    public Task<Result<Unit, BackendError>> WriteAsync(
        Device device,
        ScpiCommand command,
        CancellationToken ct
    ) => On(device, backend => backend.WriteAsync(device, command, ct));

    /// <inheritdoc/>
    public Task<Result<string, BackendError>> QueryAsync(
        Device device,
        ScpiQuery query,
        CancellationToken ct
    ) => On(device, backend => backend.QueryAsync(device, query, ct));

    /// <inheritdoc/>
    public Task<Result<string, BackendError>> ReadAsync(Device device, CancellationToken ct) =>
        On(device, backend => backend.ReadAsync(device, ct));

    /// <inheritdoc/>
    public Task<Result<Unit, BackendError>> TriggerAsync(Device device, CancellationToken ct) =>
        On(device, backend => backend.TriggerAsync(device, ct));

    /// <inheritdoc/>
    public async IAsyncEnumerable<ServiceRequest> ServiceRequestStream(
        Device device,
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        if (!_openedOn.TryGetValue(device.Name, out var backend))
        {
            yield break;
        }
        await foreach (var request in backend.ServiceRequestStream(device, ct))
        {
            yield return request;
        }
    }

    private Result<Unit, BackendError> Opened(
        Device device,
        IIviBackend backend,
        Result<Unit, BackendError> opened
    )
    {
        if (opened is Result<Unit, BackendError>.Ok)
        {
            _openedOn[device.Name] = backend;
        }
        return opened;
    }

    private Task<Result<T, BackendError>> On<T>(
        Device device,
        Func<IIviBackend, Task<Result<T, BackendError>>> operation
    ) =>
        _openedOn.TryGetValue(device.Name, out var backend)
            ? operation(backend)
            : Task.FromResult(
                Result.Failure<T, BackendError>(new TransportDisconnected("session not open"))
            );
}
