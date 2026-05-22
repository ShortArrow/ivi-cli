using IviCli.Application.Configuration;
using IviCli.Domain;
using IviCli.Domain.Configuration;

namespace IviCli.Application.Devices;

/// <summary>
/// Application-layer handler for the <c>visa list</c> query (per ADR 0003 §4
/// CQRS handler separation: Query side).
/// </summary>
public sealed class ListDevicesQueryHandler
{
    private readonly IConfigStore _store;

    /// <summary>Creates a new handler bound to the supplied configuration store.</summary>
    public ListDevicesQueryHandler(IConfigStore store)
    {
        _store = store;
    }

    /// <summary>Loads the configuration and projects it into a <see cref="DeviceListing"/>.</summary>
    public async Task<Result<DeviceListing, ListDevicesError>> HandleAsync(
        ListDevicesQuery query,
        CancellationToken ct
    )
    {
        var loadResult = await _store.LoadAsync(ct);
        return loadResult switch
        {
            Result<ConfigDocument, ConfigStoreError>.Ok ok => Result.Success<
                DeviceListing,
                ListDevicesError
            >(new DeviceListing(ok.Value.Devices, ok.Value.Defaults.Device)),
            Result<ConfigDocument, ConfigStoreError>.Error err => Result.Failure<
                DeviceListing,
                ListDevicesError
            >(new ListDevicesStorageFailure(err.Err)),
            _ => throw new InvalidOperationException("Unknown Result variant"),
        };
    }
}
