using IviCli.Application.Configuration;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Visa;
using Tomlyn;
using Tomlyn.Model;

namespace IviCli.Infrastructure.Configuration;

/// <summary>
/// Pure parser/serializer for the project's <c>config.toml</c> document.
/// Lives apart from the I/O-bound <see cref="TomlConfigStore"/> per the
/// Impureim Sandwich pattern declared in ADR 0023 §5: parsing is a pure
/// function of its input string, with no file-system access.
/// </summary>
public static class TomlConfigParser
{
    private const string DefaultsTable = "defaults";
    private const string DevicesArray = "devices";
    private const string DeviceField = "device";
    private const string NameField = "name";
    private const string ResourceField = "resource";
    private const string TimeoutMillisecondsField = "timeout_ms";

    /// <summary>
    /// Parses a TOML document into a validated <see cref="ConfigDocument"/>.
    /// </summary>
    public static Result<ConfigDocument, ConfigStoreError> Parse(string toml)
    {
        TomlTable model;
        try
        {
            model = Toml.ToModel(toml);
        }
        catch (TomlException ex)
        {
            return Fail($"TOML syntax error: {ex.Message}");
        }

        var config = ConfigDocument.Empty;

        // Devices (optional).
        if (model.TryGetValue(DevicesArray, out var devicesValue))
        {
            if (devicesValue is not TomlTableArray devicesTable)
            {
                return Fail($"expected an array of tables at [[{DevicesArray}]]");
            }

            foreach (var deviceTable in devicesTable)
            {
                var deviceResult = ParseDevice(deviceTable);
                if (deviceResult is not Result<Device, ConfigStoreError>.Ok deviceOk)
                {
                    return Result.Failure<ConfigDocument, ConfigStoreError>(
                        ((Result<Device, ConfigStoreError>.Error)deviceResult).Err
                    );
                }

                var addResult = config.AddDevice(deviceOk.Value);
                if (addResult is not Result<ConfigDocument, ConfigError>.Ok addOk)
                {
                    var addErr = ((Result<ConfigDocument, ConfigError>.Error)addResult).Err;
                    return Fail($"config validation failed: {addErr.Message}");
                }

                config = addOk.Value;
            }
        }

        // Defaults (optional).
        if (model.TryGetValue(DefaultsTable, out var defaultsValue))
        {
            if (defaultsValue is not TomlTable defaultsTable)
            {
                return Fail($"expected [{DefaultsTable}] to be a TOML table");
            }

            if (defaultsTable.TryGetValue(DeviceField, out var defaultDeviceValue))
            {
                if (defaultDeviceValue is not string defaultDeviceRaw)
                {
                    return Fail($"expected [{DefaultsTable}].{DeviceField} to be a string");
                }

                var nameResult = DeviceName.From(defaultDeviceRaw);
                if (nameResult is not Result<DeviceName, DeviceError>.Ok nameOk)
                {
                    return Fail($"invalid [{DefaultsTable}].{DeviceField}: {defaultDeviceRaw}");
                }

                var setResult = config.SetDefaultDevice(nameOk.Value);
                if (setResult is not Result<ConfigDocument, ConfigError>.Ok setOk)
                {
                    var setErr = ((Result<ConfigDocument, ConfigError>.Error)setResult).Err;
                    return Fail($"invalid default device: {setErr.Message}");
                }

                config = setOk.Value;
            }
        }

        return Result.Success<ConfigDocument, ConfigStoreError>(config);
    }

    /// <summary>
    /// Serializes a <see cref="ConfigDocument"/> back to TOML.
    /// </summary>
    public static string Serialize(ConfigDocument document)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var builder = new System.Text.StringBuilder();

        if (document.Defaults.Device is { } defaultDevice)
        {
            builder.AppendLine(inv, $"[{DefaultsTable}]");
            builder.AppendLine(inv, $"{DeviceField} = \"{defaultDevice.Value}\"");
            builder.AppendLine();
        }

        foreach (var device in document.Devices)
        {
            builder.AppendLine(inv, $"[[{DevicesArray}]]");
            builder.AppendLine(inv, $"{NameField} = \"{device.Name.Value}\"");
            builder.AppendLine(inv, $"{ResourceField} = \"{FormatResource(device.Resource)}\"");
            builder.AppendLine(inv, $"{TimeoutMillisecondsField} = {device.Timeout.Milliseconds}");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string FormatResource(VisaResource resource) =>
        resource switch
        {
            VisaResource.Tcpip t => $"TCPIP{t.Board}::{t.Host}::{t.LanDevice}::INSTR",
            VisaResource.Usb u => u.InterfaceNumber is { } iface
                ? $"USB{u.Board}::{u.VendorId}::{u.ProductId}::{u.SerialNumber}::{iface}::INSTR"
                : $"USB{u.Board}::{u.VendorId}::{u.ProductId}::{u.SerialNumber}::INSTR",
            VisaResource.Gpib g => g.SecondaryAddress is { } secondary
                ? $"GPIB{g.Board}::{g.PrimaryAddress}::{secondary}::INSTR"
                : $"GPIB{g.Board}::{g.PrimaryAddress}::INSTR",
            _ => throw new InvalidOperationException(
                $"unsupported VisaResource variant: {resource.GetType().Name}"
            ),
        };

    private static Result<Device, ConfigStoreError> ParseDevice(TomlTable table)
    {
        if (!table.TryGetValue(NameField, out var nameValue) || nameValue is not string nameRaw)
        {
            return FailDevice($"[[{DevicesArray}]] entry is missing string field '{NameField}'");
        }

        var nameResult = DeviceName.From(nameRaw);
        if (nameResult is not Result<DeviceName, DeviceError>.Ok nameOk)
        {
            return FailDevice($"invalid device name: {nameRaw}");
        }

        if (
            !table.TryGetValue(ResourceField, out var resourceValue)
            || resourceValue is not string resourceRaw
        )
        {
            return FailDevice(
                $"[[{DevicesArray}]] entry '{nameRaw}' is missing string field '{ResourceField}'"
            );
        }

        var resourceResult = VisaResource.Parse(resourceRaw);
        if (resourceResult is not Result<VisaResource, VisaResourceError>.Ok resourceOk)
        {
            return FailDevice($"invalid VISA resource for '{nameRaw}': {resourceRaw}");
        }

        if (
            !table.TryGetValue(TimeoutMillisecondsField, out var timeoutValue)
            || timeoutValue is not long timeoutMs
        )
        {
            return FailDevice(
                $"[[{DevicesArray}]] entry '{nameRaw}' is missing integer field '{TimeoutMillisecondsField}'"
            );
        }

        var timeoutResult = Timeout.FromMilliseconds((int)timeoutMs);
        if (timeoutResult is not Result<Timeout, TimeoutError>.Ok timeoutOk)
        {
            return FailDevice($"invalid timeout for '{nameRaw}': {timeoutMs}");
        }

        return Result.Success<Device, ConfigStoreError>(
            new Device(nameOk.Value, resourceOk.Value, timeoutOk.Value)
        );
    }

    private static Result<ConfigDocument, ConfigStoreError> Fail(string reason) =>
        Result.Failure<ConfigDocument, ConfigStoreError>(new ConfigStoreParseFailure(reason));

    private static Result<Device, ConfigStoreError> FailDevice(string reason) =>
        Result.Failure<Device, ConfigStoreError>(new ConfigStoreParseFailure(reason));
}
