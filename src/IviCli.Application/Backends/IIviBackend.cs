using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;

namespace IviCli.Application.Backends;

/// <summary>
/// Transport-level port for instrument communication. Implementations are in
/// <c>IviCli.Backends.*</c>; the composition root selects between them via
/// <see cref="IBackendFactory"/> (per ADR 0010 §4).
/// </summary>
/// <remarks>
/// Per ADR 0003 §4 CQRS, write and query are distinct methods rather than
/// collapsed onto a single <c>Execute</c>. All methods accept a
/// <see cref="CancellationToken"/> per ADR 0023 §7.
/// </remarks>
public interface IIviBackend
{
    /// <summary>Opens a session to the instrument addressed by <paramref name="device"/>.</summary>
    Task<Result<Unit, BackendError>> OpenAsync(Device device, CancellationToken ct);

    /// <summary>Closes any open session to <paramref name="device"/>.</summary>
    Task<Result<Unit, BackendError>> CloseAsync(Device device, CancellationToken ct);

    /// <summary>
    /// Sends a SCPI command (no response expected) to <paramref name="device"/>.
    /// </summary>
    Task<Result<Unit, BackendError>> WriteAsync(
        Device device,
        ScpiCommand command,
        CancellationToken ct
    );

    /// <summary>Sends a SCPI query and returns the textual response from <paramref name="device"/>.</summary>
    Task<Result<string, BackendError>> QueryAsync(
        Device device,
        ScpiQuery query,
        CancellationToken ct
    );

    /// <summary>
    /// Reads the next pending response from <paramref name="device"/> without
    /// transmitting anything. Used after a <see cref="WriteAsync"/> that the
    /// caller knows produces a response.
    /// </summary>
    Task<Result<string, BackendError>> ReadAsync(Device device, CancellationToken ct);

    /// <summary>
    /// Asserts a hardware trigger on <paramref name="device"/> (the IEEE
    /// 488 / SCPI <c>*TRG</c> equivalent). Backends that cannot raise a
    /// trigger return <see cref="BackendOperationNotSupported"/> rather
    /// than throwing (ADR 0041).
    /// </summary>
    Task<Result<Unit, BackendError>> TriggerAsync(Device device, CancellationToken ct);

    /// <summary>
    /// A push stream of Service Request (SRQ) events from
    /// <paramref name="device"/>. Backends without SRQ capability return
    /// an empty enumerable that completes when <paramref name="ct"/> is
    /// cancelled (ADR 0041).
    /// </summary>
    IAsyncEnumerable<ServiceRequest> ServiceRequestStream(Device device, CancellationToken ct);
}
