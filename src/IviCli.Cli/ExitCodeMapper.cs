using IviCli.Application.Configuration;
using IviCli.Application.Devices;
using IviCli.Domain;

namespace IviCli.Cli;

/// <summary>
/// Maps an <see cref="IviError"/> to a POSIX-style CLI exit code per the
/// table in ADR 0014 §4.
/// </summary>
public static class ExitCodeMapper
{
    /// <summary>Exit code returned on successful completion.</summary>
    public const int Success = 0;

    /// <summary>Generic / unclassified failure.</summary>
    public const int GenericFailure = 1;

    /// <summary>Usage error (CLI parse, argument validation).</summary>
    public const int UsageError = 2;

    /// <summary>Transport / Backend error.</summary>
    public const int TransportError = 3;

    /// <summary>Configuration error (parse, validation).</summary>
    public const int ConfigurationError = 4;

    /// <summary>Device / domain error.</summary>
    public const int DeviceError = 5;

    /// <summary>Cancelled (POSIX-style SIGINT convention).</summary>
    public const int Cancelled = 130;

    /// <summary>Maps an <see cref="IviError"/> instance to its exit code.</summary>
    public static int Map(IviError error) =>
        error switch
        {
            // Application-layer wrappers around storage failures.
            AddDeviceStorageFailure or ListDevicesStorageFailure => ConfigurationError,
            // Usage / argument-validation errors from a Command.
            AddDeviceInvalidName or AddDeviceInvalidResource or AddDeviceInvalidTimeout =>
                UsageError,
            // Device-side issues (name taken, not found).
            AddDeviceNameTaken => DeviceError,
            // Direct storage errors (when surfaced without a command wrapper).
            ConfigStoreError => ConfigurationError,
            // Anything not yet categorized.
            _ => GenericFailure,
        };
}
