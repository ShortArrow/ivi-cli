using System.Collections.Immutable;
using IviCli.Application.Configuration;
using IviCli.Application.Devices;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;

namespace IviCli.Application.Watch;

/// <summary>
/// Application-layer handler for the <c>visa watch</c> verb. Loops at
/// <see cref="WatchDevicesCommand.Interval"/>, probes every resolved
/// device once per tick via <see cref="IDeviceStatusProbe"/>, and emits
/// a <see cref="WatchTick"/> to the supplied sink. Cancellation exits
/// the loop cleanly without surfacing an exception.
/// </summary>
public sealed class WatchDevicesCommandHandler
{
    private readonly IConfigStore _configStore;
    private readonly IDeviceStatusProbe _probe;

    /// <summary>Creates a new handler.</summary>
    public WatchDevicesCommandHandler(IConfigStore configStore, IDeviceStatusProbe probe)
    {
        _configStore = configStore;
        _probe = probe;
    }

    /// <summary>Runs the watch loop until cancellation or <c>MaxIterations</c>.</summary>
    public async Task<Result<Unit, WatchDevicesError>> HandleAsync(
        WatchDevicesCommand command,
        IWatchDevicesSink sink,
        CancellationToken ct
    )
    {
        if (command.Interval <= TimeSpan.Zero)
        {
            return Result.Failure<Unit, WatchDevicesError>(
                new WatchInvalidInterval(command.Interval)
            );
        }

        var configResult = await _configStore.LoadAsync(ct);
        if (configResult is not Result<ConfigDocument, ConfigStoreError>.Ok { Value: var config })
        {
            var err = ((Result<ConfigDocument, ConfigStoreError>.Error)configResult).Err;
            return Result.Failure<Unit, WatchDevicesError>(new WatchConfigFailure(err));
        }

        var devicesResult = ResolveDevices(command.Names, config);
        if (
            devicesResult
            is not Result<ImmutableArray<Device>, WatchDevicesError>.Ok { Value: var devices }
        )
        {
            return Result.Failure<Unit, WatchDevicesError>(
                ((Result<ImmutableArray<Device>, WatchDevicesError>.Error)devicesResult).Err
            );
        }

        var sequence = 0;
        while (!ct.IsCancellationRequested)
        {
            if (command.MaxIterations is int max && sequence >= max)
            {
                break;
            }

            var probes = new Task<DeviceStatus>[devices.Length];
            for (var i = 0; i < devices.Length; i++)
            {
                probes[i] = _probe.ProbeAsync(devices[i], ct);
            }
            DeviceStatus[] snapshots;
            try
            {
                snapshots = await Task.WhenAll(probes);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var tick = new WatchTick(DateTimeOffset.UtcNow, sequence, snapshots.ToImmutableArray());
            try
            {
                await sink.EmitAsync(tick, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            sequence++;
            if (command.MaxIterations is int cap && sequence >= cap)
            {
                break;
            }

            try
            {
                await Task.Delay(command.Interval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        return Result.Success<Unit, WatchDevicesError>(Unit.Value);
    }

    private static Result<ImmutableArray<Device>, WatchDevicesError> ResolveDevices(
        ImmutableArray<string>? requested,
        ConfigDocument config
    )
    {
        if (requested is null || requested.Value.IsDefaultOrEmpty)
        {
            if (config.Devices.IsDefaultOrEmpty)
            {
                return Result.Failure<ImmutableArray<Device>, WatchDevicesError>(
                    new WatchNoDevices()
                );
            }
            return Result.Success<ImmutableArray<Device>, WatchDevicesError>(config.Devices);
        }

        var builder = ImmutableArray.CreateBuilder<Device>(requested.Value.Length);
        foreach (var raw in requested.Value)
        {
            var nameResult = DeviceName.From(raw);
            if (nameResult is not Result<DeviceName, DeviceError>.Ok { Value: var name })
            {
                return Result.Failure<ImmutableArray<Device>, WatchDevicesError>(
                    new WatchInvalidName(raw)
                );
            }
            var device = config.FindDevice(name);
            if (device is null)
            {
                return Result.Failure<ImmutableArray<Device>, WatchDevicesError>(
                    new WatchUnknownDevice(name)
                );
            }
            builder.Add(device);
        }
        return Result.Success<ImmutableArray<Device>, WatchDevicesError>(builder.ToImmutable());
    }
}
