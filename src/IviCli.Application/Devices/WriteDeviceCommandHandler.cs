using IviCli.Application.Backends;
using IviCli.Application.Configuration;
using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;

namespace IviCli.Application.Devices;

/// <summary>Application-layer handler for the <c>visa write</c> command (PRD §6.2).</summary>
public sealed class WriteDeviceCommandHandler
{
    private readonly IConfigStore _configStore;
    private readonly ISessionStore _sessionStore;
    private readonly IBackendFactory _backendFactory;

    /// <summary>Creates a new handler.</summary>
    public WriteDeviceCommandHandler(
        IConfigStore configStore,
        ISessionStore sessionStore,
        IBackendFactory backendFactory
    )
    {
        _configStore = configStore;
        _sessionStore = sessionStore;
        _backendFactory = backendFactory;
    }

    /// <summary>Validates the SCPI command, resolves the device, and writes.</summary>
    public async Task<Result<Unit, WriteDeviceError>> HandleAsync(
        WriteDeviceCommand command,
        CancellationToken ct
    )
    {
        if (
            ScpiCommand.From(command.ScpiText)
            is not Result<ScpiCommand, ScpiError>.Ok { Value: var scpi }
        )
        {
            var err = (
                (Result<ScpiCommand, ScpiError>.Error)ScpiCommand.From(command.ScpiText)
            ).Err;
            var reason = err is InvalidScpiCommand isc ? isc.Reason : "invalid";
            return Fail(new WriteDeviceInvalidScpi(command.ScpiText, reason));
        }

        var resolveResult = await DeviceResolver.ResolveAsync(
            command.Name,
            _configStore,
            _sessionStore,
            ct
        );
        if (resolveResult is not Result<Device, ResolveError>.Ok { Value: var device })
        {
            return MapResolveError(((Result<Device, ResolveError>.Error)resolveResult).Err);
        }

        var backendResult = _backendFactory.CreateFor(device);
        if (backendResult is not Result<IIviBackend, BackendError>.Ok { Value: var backend })
        {
            var err = ((Result<IIviBackend, BackendError>.Error)backendResult).Err;
            return Fail(new WriteDeviceTransportFailure(err));
        }

        var openResult = await backend.OpenAsync(device, ct);
        if (openResult is not Result<Unit, BackendError>.Ok)
        {
            var err = ((Result<Unit, BackendError>.Error)openResult).Err;
            return Fail(new WriteDeviceTransportFailure(err));
        }

        try
        {
            var writeResult = await backend.WriteAsync(device, scpi, ct);
            if (writeResult is not Result<Unit, BackendError>.Ok)
            {
                var err = ((Result<Unit, BackendError>.Error)writeResult).Err;
                return Fail(new WriteDeviceTransportFailure(err));
            }
            return Result.Success<Unit, WriteDeviceError>(Unit.Value);
        }
        finally
        {
            _ = await backend.CloseAsync(device, ct);
        }
    }

    private static Result<Unit, WriteDeviceError> Fail(WriteDeviceError error) =>
        Result.Failure<Unit, WriteDeviceError>(error);

    private static Result<Unit, WriteDeviceError> MapResolveError(ResolveError error) =>
        error switch
        {
            ResolveError.ConfigFailure c => Fail(new WriteDeviceConfigFailure(c.Inner)),
            ResolveError.SessionFailure s => Fail(new WriteDeviceSessionFailure(s.Inner)),
            ResolveError.UserFailure
            {
                Failure: { Kind: DeviceResolver.FailureKind.InvalidName, RawName: var raw },
            } => Fail(new WriteDeviceInvalidName(raw ?? "")),
            ResolveError.UserFailure { Failure.Kind: DeviceResolver.FailureKind.NoTarget } => Fail(
                new WriteDeviceNoTarget()
            ),
            ResolveError.UserFailure
            {
                Failure: { Kind: DeviceResolver.FailureKind.UnknownDevice, ResolvedName: var name },
            } when name is not null => Fail(new WriteDeviceUnknown(name)),
            _ => Fail(new WriteDeviceNoTarget()),
        };
}
