using IviCli.Application.Configuration;
using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Session;

namespace IviCli.Application.Devices;

/// <summary>
/// Shared helper for the handlers that need to translate an optional CLI
/// alias (and the session / config fallbacks) into a concrete
/// <see cref="Device"/>. Pure logic in terms of the supplied ports.
/// </summary>
public static class DeviceResolver
{
    /// <summary>Outcomes the resolver itself can surface.</summary>
    public enum FailureKind
    {
        /// <summary>The raw alias passed in failed <see cref="DeviceName"/> validation.</summary>
        InvalidName,

        /// <summary>No alias was given and there is no current / default device.</summary>
        NoTarget,

        /// <summary>The named device is not registered in the configuration.</summary>
        UnknownDevice,
    }

    /// <summary>The resolver's typed failure: the kind plus optional context.</summary>
    public sealed record Failure(
        FailureKind Kind,
        string? RawName = null,
        DeviceName? ResolvedName = null
    );

    /// <summary>
    /// Resolves a candidate alias to a configured <see cref="Device"/>.
    /// I/O failures from the supplied stores propagate as their native
    /// store-error types via the union return.
    /// </summary>
    public static async Task<Result<Device, ResolveError>> ResolveAsync(
        string? rawName,
        IConfigStore configStore,
        ISessionStore sessionStore,
        CancellationToken ct
    )
    {
        var configResult = await configStore.LoadAsync(ct);
        if (configResult is not Result<ConfigDocument, ConfigStoreError>.Ok { Value: var config })
        {
            return Result.Failure<Device, ResolveError>(
                new ResolveError.ConfigFailure(
                    ((Result<ConfigDocument, ConfigStoreError>.Error)configResult).Err
                )
            );
        }

        DeviceName name;
        if (rawName is not null)
        {
            if (
                DeviceName.From(rawName)
                is not Result<DeviceName, DeviceError>.Ok { Value: var parsed }
            )
            {
                return Result.Failure<Device, ResolveError>(
                    new ResolveError.UserFailure(new Failure(FailureKind.InvalidName, rawName))
                );
            }
            name = parsed;
        }
        else
        {
            var sessionResult = await sessionStore.LoadAsync(ct);
            if (
                sessionResult
                is not Result<SessionState, SessionStoreError>.Ok { Value: var session }
            )
            {
                return Result.Failure<Device, ResolveError>(
                    new ResolveError.SessionFailure(
                        ((Result<SessionState, SessionStoreError>.Error)sessionResult).Err
                    )
                );
            }
            var fallback = session.CurrentDevice ?? config.Defaults.Device;
            if (fallback is null)
            {
                return Result.Failure<Device, ResolveError>(
                    new ResolveError.UserFailure(new Failure(FailureKind.NoTarget))
                );
            }
            name = fallback;
        }

        var device = config.FindDevice(name);
        if (device is null)
        {
            return Result.Failure<Device, ResolveError>(
                new ResolveError.UserFailure(
                    new Failure(FailureKind.UnknownDevice, ResolvedName: name)
                )
            );
        }

        return Result.Success<Device, ResolveError>(device);
    }
}

/// <summary>Resolver error variants (storage failures or user-resolvable problems).</summary>
public abstract record ResolveError
{
    /// <summary>The config store could not be read.</summary>
    public sealed record ConfigFailure(ConfigStoreError Inner) : ResolveError;

    /// <summary>The session store could not be read.</summary>
    public sealed record SessionFailure(SessionStoreError Inner) : ResolveError;

    /// <summary>A user-side issue with the supplied / resolved name.</summary>
    public sealed record UserFailure(DeviceResolver.Failure Failure) : ResolveError;
}
