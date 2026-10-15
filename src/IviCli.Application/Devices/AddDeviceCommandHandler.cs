using IviCli.Application.Audit;
using IviCli.Application.Configuration;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;

namespace IviCli.Application.Devices;

/// <summary>
/// Application-layer handler for the <c>visa add</c> command (per ADR 0003 §4
/// CQRS handler separation).
/// </summary>
/// <remarks>
/// The handler validates the raw <see cref="AddDeviceCommand"/> into Domain
/// Value Objects, loads the current <see cref="ConfigDocument"/>, applies the
/// addition (which enforces the cross-entity uniqueness invariant), and saves.
/// Each failure path is mapped at this layer's boundary into a typed
/// <see cref="AddDeviceError"/> variant (per ADR 0014 §5).
/// </remarks>
public sealed class AddDeviceCommandHandler
{
    private readonly IConfigStore _store;
    private readonly IAuditLog _audit;
    private readonly IAuditSubject _subject;
    private readonly TimeProvider _time;

    /// <summary>
    /// Creates a new handler bound to the supplied configuration store.
    /// <paramref name="audit"/> + <paramref name="subject"/> emit a
    /// <see cref="ConfigMutated"/> event on successful save (ADR 0043).
    /// </summary>
    public AddDeviceCommandHandler(
        IConfigStore store,
        IAuditLog? audit = null,
        IAuditSubject? subject = null,
        TimeProvider? time = null
    )
    {
        _store = store;
        _audit = audit ?? NullAuditLog.Instance;
        _subject = subject ?? new StaticAuditSubject("unknown");
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Validates the command, loads the configuration, adds the device, and
    /// persists the result.
    /// </summary>
    /// <returns>
    /// The added device; its <see cref="Device.IgnoresTermination"/> tells the
    /// caller to warn about a terminator that has no effect.
    /// </returns>
    public async Task<Result<Device, AddDeviceError>> HandleAsync(
        AddDeviceCommand command,
        CancellationToken ct
    )
    {
        // Validate inputs into Domain Value Objects (Anti-Corruption Layer).
        if (
            DeviceName.From(command.Name)
            is not Result<DeviceName, DeviceError>.Ok { Value: var name }
        )
        {
            return Fail(new AddDeviceInvalidName(command.Name));
        }

        if (
            VisaResource.Parse(command.Resource)
            is not Result<VisaResource, VisaResourceError>.Ok { Value: var resource }
        )
        {
            return Fail(new AddDeviceInvalidResource(command.Resource));
        }

        if (
            Timeout.FromMilliseconds(command.TimeoutMilliseconds)
            is not Result<Timeout, TimeoutError>.Ok { Value: var timeout }
        )
        {
            return Fail(new AddDeviceInvalidTimeout(command.TimeoutMilliseconds));
        }

        var device = new Device(name, resource, timeout);
        if (command.Serial is { } serialOptions)
        {
            if (!device.IsSerial)
            {
                return Fail(new AddDeviceSerialOnNonSerial(name));
            }
            switch (ParseSerial(serialOptions))
            {
                case Result<SerialSettings, AddDeviceError>.Error invalid:
                    return Fail(invalid.Err);
                case Result<SerialSettings, AddDeviceError>.Ok serial:
                    device = device with { Serial = serial.Value };
                    break;
            }
        }
        if (command.ReadTermination is { } readOption)
        {
            if (ParseTermination(readOption) is not { } read)
            {
                return Fail(new AddDeviceInvalidTermination(readOption));
            }
            device = device with { ReadTermination = read };
        }
        if (command.WriteTermination is { } writeOption)
        {
            if (ParseTermination(writeOption) is not { } write)
            {
                return Fail(new AddDeviceInvalidTermination(writeOption));
            }
            device = device with { WriteTermination = write };
        }

        // Load the current configuration.
        var loadResult = await _store.LoadAsync(ct);
        if (loadResult is not Result<ConfigDocument, ConfigStoreError>.Ok { Value: var config })
        {
            var loadErr = ((Result<ConfigDocument, ConfigStoreError>.Error)loadResult).Err;
            return Fail(new AddDeviceStorageFailure(loadErr));
        }

        // Apply the addition.
        var addResult = config.AddDevice(device);
        if (addResult is not Result<ConfigDocument, ConfigError>.Ok { Value: var updatedConfig })
        {
            var addErr = ((Result<ConfigDocument, ConfigError>.Error)addResult).Err;
            return Fail(MapConfigError(addErr, name));
        }

        // Persist.
        var saveResult = await _store.SaveAsync(updatedConfig, ct);
        if (saveResult is not Result<Unit, ConfigStoreError>.Ok)
        {
            var saveErr = ((Result<Unit, ConfigStoreError>.Error)saveResult).Err;
            return Fail(new AddDeviceStorageFailure(saveErr));
        }

        await _audit.AppendAsync(
            new ConfigMutated(_time.GetUtcNow(), "device.add", name.Value, _subject.Get()),
            ct
        );

        return Result.Success<Device, AddDeviceError>(device);
    }

    /// <summary>
    /// The settings <paramref name="options"/> describe, each omitted one
    /// kept at VISA's default.
    /// </summary>
    private static Result<SerialSettings, AddDeviceError> ParseSerial(SerialOptions options)
    {
        var defaults = SerialSettings.Default;
        if (
            ParseName(options.Parity, defaults.Parity, SerialSettingNames.ParseParity)
            is not { } parity
        )
        {
            return SerialFail("parity", options.Parity!);
        }
        if (
            ParseName(options.StopBits, defaults.StopBits, SerialSettingNames.ParseStopBits)
            is not { } stopBits
        )
        {
            return SerialFail("stop_bits", options.StopBits!);
        }
        if (
            ParseName(
                options.FlowControl,
                defaults.FlowControl,
                SerialSettingNames.ParseFlowControl
            )
            is not { } flowControl
        )
        {
            return SerialFail("flow_control", options.FlowControl!);
        }
        var baud = options.Baud ?? defaults.Baud;
        var dataBits = options.DataBits ?? defaults.DataBits;
        return SerialSettings.From(baud, dataBits, parity, stopBits, flowControl) switch
        {
            Result<SerialSettings, DeviceError>.Ok ok => Result.Success<
                SerialSettings,
                AddDeviceError
            >(ok.Value),
            Result<SerialSettings, DeviceError>.Error { Err: InvalidBaudRate } => SerialFail(
                "baud",
                baud.ToString(System.Globalization.CultureInfo.InvariantCulture)
            ),
            _ => SerialFail(
                "data_bits",
                dataBits.ToString(System.Globalization.CultureInfo.InvariantCulture)
            ),
        };
    }

    private static T? ParseName<T>(string? name, T fallback, Func<string, T?> parse)
        where T : struct => name is null ? fallback : parse(name);

    private static Result<SerialSettings, AddDeviceError> SerialFail(string field, string raw) =>
        Result.Failure<SerialSettings, AddDeviceError>(
            new AddDeviceInvalidSerialSetting(field, raw)
        );

    /// <summary>
    /// The terminator an option names: <c>lf</c>, <c>cr</c> or <c>crlf</c> in
    /// any letter case, or the characters written with <c>\n</c> and <c>\r</c>
    /// escapes; <see langword="null"/> for anything else.
    /// </summary>
    private static Termination? ParseTermination(string option)
    {
        var chars = option.ToLowerInvariant() switch
        {
            "lf" => "\n",
            "cr" => "\r",
            "crlf" => "\r\n",
            _ => option
                .Replace(@"\r", "\r", StringComparison.Ordinal)
                .Replace(@"\n", "\n", StringComparison.Ordinal),
        };
        return Termination.From(chars) is Result<Termination, DeviceError>.Ok ok ? ok.Value : null;
    }

    private static Result<Device, AddDeviceError> Fail(AddDeviceError error) =>
        Result.Failure<Device, AddDeviceError>(error);

    private static AddDeviceError MapConfigError(ConfigError error, DeviceName candidate) =>
        error switch
        {
            DuplicateDeviceName d => new AddDeviceNameTaken(d.Name),
            SerialSettingsOnNonSerialDevice s => new AddDeviceSerialOnNonSerial(s.Name),
            // Other ConfigError variants (DeviceNotFound, DefaultDeviceMissing) are
            // impossible from AddDevice; fall back defensively.
            _ => new AddDeviceNameTaken(candidate),
        };
}
