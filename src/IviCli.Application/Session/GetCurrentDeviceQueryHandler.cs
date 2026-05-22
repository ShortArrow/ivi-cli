using IviCli.Application.Configuration;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Session;

namespace IviCli.Application.Session;

/// <summary>
/// Application-layer handler for the <c>visa current</c> query. Resolves the
/// effective current device: the volatile session pointer takes precedence
/// over the persisted <c>config.toml</c> <c>[defaults].device</c>; either may
/// be null.
/// </summary>
public sealed class GetCurrentDeviceQueryHandler
{
    private readonly IConfigStore _configStore;
    private readonly ISessionStore _sessionStore;

    /// <summary>Creates a new handler bound to the supplied stores.</summary>
    public GetCurrentDeviceQueryHandler(IConfigStore configStore, ISessionStore sessionStore)
    {
        _configStore = configStore;
        _sessionStore = sessionStore;
    }

    /// <summary>Resolves the current device.</summary>
    public async Task<Result<CurrentDevice, GetCurrentDeviceError>> HandleAsync(
        GetCurrentDeviceQuery query,
        CancellationToken ct
    )
    {
        var sessionResult = await _sessionStore.LoadAsync(ct);
        if (sessionResult is not Result<SessionState, SessionStoreError>.Ok { Value: var session })
        {
            var err = ((Result<SessionState, SessionStoreError>.Error)sessionResult).Err;
            return Result.Failure<CurrentDevice, GetCurrentDeviceError>(
                new GetCurrentDeviceSessionFailure(err)
            );
        }

        if (session.CurrentDevice is { } sessionDevice)
        {
            return Result.Success<CurrentDevice, GetCurrentDeviceError>(
                new CurrentDevice(sessionDevice)
            );
        }

        // Fall back to the persisted default device when the session is empty.
        var configResult = await _configStore.LoadAsync(ct);
        if (
            configResult is Result<ConfigDocument, ConfigStoreError>.Ok { Value: var config }
            && config.Defaults.Device is { } defaultDevice
        )
        {
            return Result.Success<CurrentDevice, GetCurrentDeviceError>(
                new CurrentDevice(defaultDevice)
            );
        }

        return Result.Success<CurrentDevice, GetCurrentDeviceError>(new CurrentDevice(null));
    }
}
