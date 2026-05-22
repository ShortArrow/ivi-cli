using IviCli.Application.Backends;
using IviCli.Application.Configuration;
using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Devices;

namespace IviCli.Application.Devices;

/// <summary>Application-layer handler for the <c>visa read</c> command (PRD §6.2).</summary>
public sealed class ReadDeviceCommandHandler
{
    private readonly IConfigStore _configStore;
    private readonly ISessionStore _sessionStore;
    private readonly IBackendFactory _backendFactory;

    /// <summary>Creates a new handler.</summary>
    public ReadDeviceCommandHandler(
        IConfigStore configStore,
        ISessionStore sessionStore,
        IBackendFactory backendFactory
    )
    {
        _configStore = configStore;
        _sessionStore = sessionStore;
        _backendFactory = backendFactory;
    }

    /// <summary>Resolves the device and reads any pending response.</summary>
    public async Task<Result<string, ReadDeviceError>> HandleAsync(
        ReadDeviceCommand command,
        CancellationToken ct
    )
    {
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
            return Fail(new ReadDeviceTransportFailure(err));
        }

        var openResult = await backend.OpenAsync(device, ct);
        if (openResult is not Result<Unit, BackendError>.Ok)
        {
            var err = ((Result<Unit, BackendError>.Error)openResult).Err;
            return Fail(new ReadDeviceTransportFailure(err));
        }

        try
        {
            var readResult = await backend.ReadAsync(device, ct);
            if (readResult is not Result<string, BackendError>.Ok { Value: var response })
            {
                var err = ((Result<string, BackendError>.Error)readResult).Err;
                return Fail(new ReadDeviceTransportFailure(err));
            }
            return Result.Success<string, ReadDeviceError>(response);
        }
        finally
        {
            _ = await backend.CloseAsync(device, ct);
        }
    }

    private static Result<string, ReadDeviceError> Fail(ReadDeviceError error) =>
        Result.Failure<string, ReadDeviceError>(error);

    private static Result<string, ReadDeviceError> MapResolveError(ResolveError error) =>
        error switch
        {
            ResolveError.ConfigFailure c => Fail(new ReadDeviceConfigFailure(c.Inner)),
            ResolveError.SessionFailure s => Fail(new ReadDeviceSessionFailure(s.Inner)),
            ResolveError.UserFailure
            {
                Failure: { Kind: DeviceResolver.FailureKind.InvalidName, RawName: var raw },
            } => Fail(new ReadDeviceInvalidName(raw ?? "")),
            ResolveError.UserFailure { Failure.Kind: DeviceResolver.FailureKind.NoTarget } => Fail(
                new ReadDeviceNoTarget()
            ),
            ResolveError.UserFailure
            {
                Failure: { Kind: DeviceResolver.FailureKind.UnknownDevice, ResolvedName: var name },
            } when name is not null => Fail(new ReadDeviceUnknown(name)),
            _ => Fail(new ReadDeviceNoTarget()),
        };
}
