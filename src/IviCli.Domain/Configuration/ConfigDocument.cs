using System.Collections.Immutable;
using IviCli.Domain.Devices;

namespace IviCli.Domain.Configuration;

/// <summary>
/// The structured, validated form of <c>config.toml</c>. Holds the configured
/// devices and the project-wide defaults; servers and routes are added when
/// Phase 2 work begins.
/// </summary>
/// <remarks>
/// <para>
/// All mutators return a new <see cref="ConfigDocument"/> (per the immutability
/// rule in ADR 0023). Cross-entity invariants — device name uniqueness, the
/// default device being a member of the device list — are enforced by the
/// mutators themselves, so any reachable <see cref="ConfigDocument"/> is known
/// to be self-consistent.
/// </para>
/// </remarks>
public sealed record ConfigDocument
{
    /// <summary>An empty configuration: no devices, no defaults.</summary>
    public static ConfigDocument Empty { get; } =
        new(devices: ImmutableArray<Device>.Empty, defaults: Defaults.None);

    /// <summary>The configured devices, in insertion order.</summary>
    public ImmutableArray<Device> Devices { get; }

    /// <summary>The <c>[defaults]</c> section.</summary>
    public Defaults Defaults { get; }

    private ConfigDocument(ImmutableArray<Device> devices, Defaults defaults)
    {
        Devices = devices;
        Defaults = defaults;
    }

    /// <summary>
    /// Structural equality. The auto-generated <c>record</c> equality compares
    /// <see cref="ImmutableArray{T}"/> by reference, so we override to compare
    /// devices element-wise.
    /// </summary>
    public bool Equals(ConfigDocument? other) =>
        other is not null && Defaults == other.Defaults && Devices.SequenceEqual(other.Devices);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Defaults);
        foreach (var device in Devices)
        {
            hash.Add(device);
        }
        return hash.ToHashCode();
    }

    /// <summary>Finds a device by alias, or returns <see langword="null"/>.</summary>
    public Device? FindDevice(DeviceName name)
    {
        foreach (var device in Devices)
        {
            if (device.Name == name)
            {
                return device;
            }
        }
        return null;
    }

    /// <summary>
    /// Returns a new <see cref="ConfigDocument"/> with <paramref name="device"/> appended.
    /// Fails with <see cref="DuplicateDeviceName"/> if the alias already exists.
    /// </summary>
    public Result<ConfigDocument, ConfigError> AddDevice(Device device)
    {
        if (FindDevice(device.Name) is not null)
        {
            return Result.Failure<ConfigDocument, ConfigError>(
                new DuplicateDeviceName(device.Name)
            );
        }
        return Result.Success<ConfigDocument, ConfigError>(
            new ConfigDocument(Devices.Add(device), Defaults)
        );
    }

    /// <summary>
    /// Returns a new <see cref="ConfigDocument"/> with the named device removed.
    /// If the removed device was the current default, the default is cleared.
    /// Fails with <see cref="DeviceNotFound"/> if no such device exists.
    /// </summary>
    public Result<ConfigDocument, ConfigError> RemoveDevice(DeviceName name)
    {
        var existing = FindDevice(name);
        if (existing is null)
        {
            return Result.Failure<ConfigDocument, ConfigError>(new DeviceNotFound(name));
        }

        var nextDevices = Devices.Remove(existing);
        var nextDefaults = Defaults.Device == name ? Defaults with { Device = null } : Defaults;
        return Result.Success<ConfigDocument, ConfigError>(
            new ConfigDocument(nextDevices, nextDefaults)
        );
    }

    /// <summary>
    /// Returns a new <see cref="ConfigDocument"/> with the default device set
    /// (or cleared when <paramref name="name"/> is <see langword="null"/>).
    /// Fails with <see cref="DefaultDeviceMissing"/> when the candidate device
    /// is not present in <see cref="Devices"/>.
    /// </summary>
    public Result<ConfigDocument, ConfigError> SetDefaultDevice(DeviceName? name)
    {
        if (name is not null && FindDevice(name) is null)
        {
            return Result.Failure<ConfigDocument, ConfigError>(new DefaultDeviceMissing(name));
        }
        return Result.Success<ConfigDocument, ConfigError>(
            new ConfigDocument(Devices, Defaults with { Device = name })
        );
    }
}
