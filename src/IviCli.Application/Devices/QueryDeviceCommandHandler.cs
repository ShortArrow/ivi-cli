using IviCli.Application.Backends;
using IviCli.Application.Configuration;
using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Session;

namespace IviCli.Application.Devices;

/// <summary>
/// Application-layer handler for the <c>visa query</c> command (PRD §6.2).
/// Resolves the target device (explicit name or session/config default),
/// hands the validated <see cref="ScpiQuery"/> to the resolved Backend,
/// and returns the response string.
/// </summary>
public sealed class QueryDeviceCommandHandler
{
    private readonly IConfigStore _configStore;
    private readonly ISessionStore _sessionStore;
    private readonly IBackendFactory _backendFactory;

    /// <summary>Creates a new handler bound to the supplied dependencies.</summary>
    public QueryDeviceCommandHandler(
        IConfigStore configStore,
        ISessionStore sessionStore,
        IBackendFactory backendFactory
    )
    {
        _configStore = configStore;
        _sessionStore = sessionStore;
        _backendFactory = backendFactory;
    }

    /// <summary>Executes the SCPI query against the resolved device.</summary>
    public async Task<Result<string, QueryDeviceError>> HandleAsync(
        QueryDeviceCommand command,
        CancellationToken ct
    )
    {
        // Parse the SCPI text into the validated VO.
        if (
            ScpiQuery.From(command.ScpiText)
            is not Result<ScpiQuery, ScpiError>.Ok { Value: var scpi }
        )
        {
            var err = ((Result<ScpiQuery, ScpiError>.Error)ScpiQuery.From(command.ScpiText)).Err;
            var reason = err is InvalidScpiQuery isq ? isq.Reason : "invalid";
            return Fail(new QueryDeviceInvalidScpi(command.ScpiText, reason));
        }

        // Load the configuration up front; we need it whether the name was
        // explicit or implicit.
        var configResult = await _configStore.LoadAsync(ct);
        if (configResult is not Result<ConfigDocument, ConfigStoreError>.Ok { Value: var config })
        {
            var err = ((Result<ConfigDocument, ConfigStoreError>.Error)configResult).Err;
            return Fail(new QueryDeviceConfigFailure(err));
        }

        // Resolve the target name.
        DeviceName? targetName;
        if (command.Name is { } rawName)
        {
            if (
                DeviceName.From(rawName)
                is not Result<DeviceName, DeviceError>.Ok { Value: var parsed }
            )
            {
                return Fail(new QueryDeviceInvalidName(rawName));
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
                var err = ((Result<SessionState, SessionStoreError>.Error)sessionResult).Err;
                return Fail(new QueryDeviceSessionFailure(err));
            }
            targetName = session.CurrentDevice ?? config.Defaults.Device;
            if (targetName is null)
            {
                return Fail(new QueryDeviceNoTarget());
            }
        }

        var device = config.FindDevice(targetName);
        if (device is null)
        {
            return Fail(new QueryDeviceUnknown(targetName));
        }

        var backendResult = _backendFactory.CreateFor(device);
        if (backendResult is not Result<IIviBackend, BackendError>.Ok { Value: var backend })
        {
            var err = ((Result<IIviBackend, BackendError>.Error)backendResult).Err;
            return Fail(new QueryDeviceTransportFailure(err));
        }

        var openResult = await backend.OpenAsync(device, ct);
        if (openResult is not Result<Unit, BackendError>.Ok)
        {
            var err = ((Result<Unit, BackendError>.Error)openResult).Err;
            return Fail(new QueryDeviceTransportFailure(err));
        }

        try
        {
            var queryResult = await backend.QueryAsync(device, scpi, ct);
            if (queryResult is not Result<string, BackendError>.Ok { Value: var response })
            {
                var err = ((Result<string, BackendError>.Error)queryResult).Err;
                return Fail(new QueryDeviceTransportFailure(err));
            }
            return Result.Success<string, QueryDeviceError>(response);
        }
        finally
        {
            // Best-effort close; surface a transport error from query above
            // rather than masking it with a close failure.
            _ = await backend.CloseAsync(device, ct);
        }
    }

    private static Result<string, QueryDeviceError> Fail(QueryDeviceError error) =>
        Result.Failure<string, QueryDeviceError>(error);
}
