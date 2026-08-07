using IviCli.Application.Configuration;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Servers;
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
    private const string ServersArray = "servers";
    private const string RoutesArray = "routes";
    private const string PoolTable = "pool";
    private const string DeviceField = "device";
    private const string NameField = "name";
    private const string TypeField = "type";
    private const string BindField = "bind";
    private const string PortField = "port";
    private const string ServerField = "server";
    private const string EndpointField = "endpoint";
    private const string ResourceField = "resource";
    private const string TimeoutMillisecondsField = "timeout_ms";
    private const string PoolEnabledField = "enabled";
    private const string PoolIdleTimeoutField = "idle_timeout";
    private const string PoolMaxDevicesField = "max_devices";
    private const string ApiTable = "api";
    private const string ApiTlsTable = "tls";
    private const string TlsEnabledField = "enabled";
    private const string TlsCertPathField = "cert_path";
    private const string TlsKeyPathField = "key_path";
    private const string TlsPasswordEnvField = "password_env";
    private const string TlsSelfSignedField = "self_signed";
    private const string TlsClientRequiredField = "client_required";
    private const string TlsClientCaPathField = "client_ca_path";
    private const string TelemetryTable = "telemetry";
    private const string TelemetryEnabledField = "enabled";
    private const string TelemetryOtlpEndpointField = "otlp_endpoint";
    private const string TelemetryServiceNameField = "service_name";
    private const string TelemetryTracesEnabledField = "traces_enabled";
    private const string TelemetryMetricsEnabledField = "metrics_enabled";
    private const string AuditTable = "audit";
    private const string AuditEnabledField = "enabled";
    private const string AuditPathField = "path";
    private const string PluginsTable = "plugins";
    private const string PluginsEnabledField = "enabled";
    private const string PluginsAllowedField = "allowed";

    /// <summary>
    /// Parses a TOML document into a validated <see cref="ConfigDocument"/>.
    /// </summary>
    public static Result<ConfigDocument, ConfigStoreError> Parse(string toml)
    {
        TomlTable model;
        try
        {
            model = TomlSerializer.Deserialize<TomlTable>(toml) ?? new TomlTable();
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

        // Servers (optional).
        if (model.TryGetValue(ServersArray, out var serversValue))
        {
            if (serversValue is not TomlTableArray serversTable)
            {
                return Fail($"expected an array of tables at [[{ServersArray}]]");
            }
            foreach (var serverTable in serversTable)
            {
                var serverResult = ParseServer(serverTable);
                if (serverResult is not Result<Server, ConfigStoreError>.Ok serverOk)
                {
                    return Result.Failure<ConfigDocument, ConfigStoreError>(
                        ((Result<Server, ConfigStoreError>.Error)serverResult).Err
                    );
                }
                var addResult = config.AddServer(serverOk.Value);
                if (addResult is not Result<ConfigDocument, ConfigError>.Ok serverAddOk)
                {
                    return Fail(
                        $"config validation failed: "
                            + ((Result<ConfigDocument, ConfigError>.Error)addResult).Err.Message
                    );
                }
                config = serverAddOk.Value;
            }
        }

        // Routes (optional). Must be parsed after servers + devices so the
        // cross-entity invariants in AddRoute can fire.
        if (model.TryGetValue(RoutesArray, out var routesValue))
        {
            if (routesValue is not TomlTableArray routesTable)
            {
                return Fail($"expected an array of tables at [[{RoutesArray}]]");
            }
            foreach (var routeTable in routesTable)
            {
                var routeResult = ParseRoute(routeTable);
                if (routeResult is not Result<Route, ConfigStoreError>.Ok routeOk)
                {
                    return Result.Failure<ConfigDocument, ConfigStoreError>(
                        ((Result<Route, ConfigStoreError>.Error)routeResult).Err
                    );
                }
                var addResult = config.AddRoute(routeOk.Value);
                if (addResult is not Result<ConfigDocument, ConfigError>.Ok routeAddOk)
                {
                    return Fail(
                        $"config validation failed: "
                            + ((Result<ConfigDocument, ConfigError>.Error)addResult).Err.Message
                    );
                }
                config = routeAddOk.Value;
            }
        }

        // Pool (optional).
        if (model.TryGetValue(PoolTable, out var poolValue))
        {
            if (poolValue is not TomlTable poolTable)
            {
                return Fail($"expected [{PoolTable}] to be a TOML table");
            }
            var poolResult = ParsePool(poolTable);
            if (poolResult is not Result<PoolConfig, ConfigStoreError>.Ok poolOk)
            {
                return Result.Failure<ConfigDocument, ConfigStoreError>(
                    ((Result<PoolConfig, ConfigStoreError>.Error)poolResult).Err
                );
            }
            config = config.WithPool(poolOk.Value);
        }

        // API (optional, with nested [api.tls]).
        if (model.TryGetValue(ApiTable, out var apiValue))
        {
            if (apiValue is not TomlTable apiTable)
            {
                return Fail($"expected [{ApiTable}] to be a TOML table");
            }
            var apiResult = ParseApi(apiTable);
            if (apiResult is not Result<ApiConfig, ConfigStoreError>.Ok apiOk)
            {
                return Result.Failure<ConfigDocument, ConfigStoreError>(
                    ((Result<ApiConfig, ConfigStoreError>.Error)apiResult).Err
                );
            }
            config = config.WithApi(apiOk.Value);
        }

        // Telemetry (optional, ADR 0040).
        if (model.TryGetValue(TelemetryTable, out var telemetryValue))
        {
            if (telemetryValue is not TomlTable telemetryTable)
            {
                return Fail($"expected [{TelemetryTable}] to be a TOML table");
            }
            var tResult = ParseTelemetry(telemetryTable);
            if (tResult is not Result<TelemetryConfig, ConfigStoreError>.Ok tOk)
            {
                return Result.Failure<ConfigDocument, ConfigStoreError>(
                    ((Result<TelemetryConfig, ConfigStoreError>.Error)tResult).Err
                );
            }
            config = config.WithTelemetry(tOk.Value);
        }

        // Audit (optional, ADR 0043).
        if (model.TryGetValue(AuditTable, out var auditValue))
        {
            if (auditValue is not TomlTable auditTable)
            {
                return Fail($"expected [{AuditTable}] to be a TOML table");
            }
            var aResult = ParseAudit(auditTable);
            if (aResult is not Result<AuditConfig, ConfigStoreError>.Ok aOk)
            {
                return Result.Failure<ConfigDocument, ConfigStoreError>(
                    ((Result<AuditConfig, ConfigStoreError>.Error)aResult).Err
                );
            }
            config = config.WithAudit(aOk.Value);
        }

        // Plugins (optional, ADR 0013).
        if (model.TryGetValue(PluginsTable, out var pluginsValue))
        {
            if (pluginsValue is not TomlTable pluginsTable)
            {
                return Fail($"expected [{PluginsTable}] to be a TOML table");
            }
            var pResult = ParsePlugins(pluginsTable);
            if (pResult is not Result<PluginsConfig, ConfigStoreError>.Ok pOk)
            {
                return Result.Failure<ConfigDocument, ConfigStoreError>(
                    ((Result<PluginsConfig, ConfigStoreError>.Error)pResult).Err
                );
            }
            config = config.WithPlugins(pOk.Value);
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

        if (document.Pool != PoolConfig.Default)
        {
            builder.AppendLine(inv, $"[{PoolTable}]");
            builder.AppendLine(
                inv,
                $"{PoolEnabledField} = {(document.Pool.Enabled ? "true" : "false")}"
            );
            builder.AppendLine(
                inv,
                $"{PoolIdleTimeoutField} = \"{FormatDuration(document.Pool.IdleTimeout)}\""
            );
            builder.AppendLine(inv, $"{PoolMaxDevicesField} = {document.Pool.MaxDevices}");
            builder.AppendLine();
        }

        if (document.Api != ApiConfig.Default)
        {
            var tls = document.Api.Tls;
            builder.AppendLine(inv, $"[{ApiTable}.{ApiTlsTable}]");
            builder.AppendLine(inv, $"{TlsEnabledField} = {(tls.Enabled ? "true" : "false")}");
            if (tls.CertPath is not null)
            {
                builder.AppendLine(inv, $"{TlsCertPathField} = \"{tls.CertPath}\"");
            }
            if (tls.KeyPath is not null)
            {
                builder.AppendLine(inv, $"{TlsKeyPathField} = \"{tls.KeyPath}\"");
            }
            if (tls.PasswordEnv is not null)
            {
                builder.AppendLine(inv, $"{TlsPasswordEnvField} = \"{tls.PasswordEnv}\"");
            }
            if (tls.SelfSigned)
            {
                builder.AppendLine(inv, $"{TlsSelfSignedField} = true");
            }
            if (tls.ClientRequired)
            {
                builder.AppendLine(inv, $"{TlsClientRequiredField} = true");
            }
            if (tls.ClientCaPath is not null)
            {
                builder.AppendLine(inv, $"{TlsClientCaPathField} = \"{tls.ClientCaPath}\"");
            }
            builder.AppendLine();
        }

        if (document.Telemetry != TelemetryConfig.Default)
        {
            var t = document.Telemetry;
            builder.AppendLine(inv, $"[{TelemetryTable}]");
            builder.AppendLine(inv, $"{TelemetryEnabledField} = {(t.Enabled ? "true" : "false")}");
            if (t.OtlpEndpoint is not null)
            {
                builder.AppendLine(inv, $"{TelemetryOtlpEndpointField} = \"{t.OtlpEndpoint}\"");
            }
            builder.AppendLine(inv, $"{TelemetryServiceNameField} = \"{t.ServiceName}\"");
            builder.AppendLine(
                inv,
                $"{TelemetryTracesEnabledField} = {(t.TracesEnabled ? "true" : "false")}"
            );
            builder.AppendLine(
                inv,
                $"{TelemetryMetricsEnabledField} = {(t.MetricsEnabled ? "true" : "false")}"
            );
            builder.AppendLine();
        }

        if (document.Audit != AuditConfig.Default)
        {
            var a = document.Audit;
            builder.AppendLine(inv, $"[{AuditTable}]");
            builder.AppendLine(inv, $"{AuditEnabledField} = {(a.Enabled ? "true" : "false")}");
            if (a.Path is not null)
            {
                builder.AppendLine(inv, $"{AuditPathField} = \"{a.Path}\"");
            }
            builder.AppendLine();
        }

        if (document.Plugins != PluginsConfig.Default)
        {
            var p = document.Plugins;
            builder.AppendLine(inv, $"[{PluginsTable}]");
            builder.AppendLine(inv, $"{PluginsEnabledField} = {(p.Enabled ? "true" : "false")}");
            if (!p.Allowed.IsDefaultOrEmpty)
            {
                var entries = string.Join(", ", p.Allowed.Select(n => $"\"{n}\""));
                builder.AppendLine(inv, $"{PluginsAllowedField} = [{entries}]");
            }
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

        foreach (var server in document.Servers)
        {
            builder.AppendLine(inv, $"[[{ServersArray}]]");
            builder.AppendLine(inv, $"{NameField} = \"{server.Name.Value}\"");
            builder.AppendLine(inv, $"{TypeField} = \"{FormatServerType(server.Type)}\"");
            builder.AppendLine(inv, $"{BindField} = \"{server.Bind.Value}\"");
            builder.AppendLine(inv, $"{PortField} = {server.Port.Value}");
            builder.AppendLine();
        }

        foreach (var route in document.Routes)
        {
            builder.AppendLine(inv, $"[[{RoutesArray}]]");
            builder.AppendLine(inv, $"{ServerField} = \"{route.ServerName.Value}\"");
            builder.AppendLine(inv, $"{EndpointField} = \"{route.Endpoint.Value}\"");
            builder.AppendLine(inv, $"{DeviceField} = \"{route.DeviceName.Value}\"");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string FormatServerType(ServerType type) =>
        type switch
        {
            ServerType.Local => "local",
            ServerType.Socket => "socket",
            ServerType.HiSlip => "hislip",
            ServerType.Vxi11 => "vxi11",
            ServerType.UsbIp => "usbip",
            _ => "local",
        };

    private static Result<Server, ConfigStoreError> ParseServer(TomlTable table)
    {
        if (!table.TryGetValue(NameField, out var nameValue) || nameValue is not string nameRaw)
        {
            return FailServer($"[[{ServersArray}]] entry is missing string field '{NameField}'");
        }
        var nameResult = ServerName.From(nameRaw);
        if (nameResult is not Result<ServerName, ServerNameError>.Ok nameOk)
        {
            return FailServer($"invalid server name: {nameRaw}");
        }

        if (!table.TryGetValue(TypeField, out var typeValue) || typeValue is not string typeRaw)
        {
            return FailServer($"[[{ServersArray}]] entry '{nameRaw}' missing '{TypeField}'");
        }
        var type = typeRaw.ToLowerInvariant() switch
        {
            "local" => ServerType.Local,
            "socket" => ServerType.Socket,
            "hislip" => ServerType.HiSlip,
            "vxi11" => ServerType.Vxi11,
            "usbip" => ServerType.UsbIp,
            _ => (ServerType?)null,
        };
        if (type is null)
        {
            return FailServer($"unknown server type '{typeRaw}' for '{nameRaw}'");
        }

        var bindRaw = table.TryGetValue(BindField, out var bv) && bv is string b ? b : "127.0.0.1";
        var bindResult = IpAddress.From(bindRaw);
        if (bindResult is not Result<IpAddress, IpAddressError>.Ok bindOk)
        {
            return FailServer($"invalid bind for '{nameRaw}': {bindRaw}");
        }

        if (!table.TryGetValue(PortField, out var portValue) || portValue is not long portLong)
        {
            return FailServer($"[[{ServersArray}]] entry '{nameRaw}' missing '{PortField}'");
        }
        var portResult = Port.From((int)portLong);
        if (portResult is not Result<Port, PortError>.Ok portOk)
        {
            return FailServer($"invalid port for '{nameRaw}': {portLong}");
        }

        return Result.Success<Server, ConfigStoreError>(
            new Server(nameOk.Value, type.Value, bindOk.Value, portOk.Value)
        );
    }

    private static Result<Route, ConfigStoreError> ParseRoute(TomlTable table)
    {
        if (
            !table.TryGetValue(ServerField, out var serverValue)
            || serverValue is not string serverRaw
        )
        {
            return FailRoute($"[[{RoutesArray}]] entry missing '{ServerField}'");
        }
        var serverNameResult = ServerName.From(serverRaw);
        if (serverNameResult is not Result<ServerName, ServerNameError>.Ok serverNameOk)
        {
            return FailRoute($"invalid route server name: {serverRaw}");
        }

        if (
            !table.TryGetValue(EndpointField, out var endpointValue)
            || endpointValue is not string endpointRaw
        )
        {
            return FailRoute($"[[{RoutesArray}]] entry missing '{EndpointField}'");
        }
        var endpointResult = PublicEndpoint.From(endpointRaw);
        if (endpointResult is not Result<PublicEndpoint, PublicEndpointError>.Ok endpointOk)
        {
            return FailRoute($"invalid route endpoint: {endpointRaw}");
        }

        if (
            !table.TryGetValue(DeviceField, out var deviceValue)
            || deviceValue is not string deviceRaw
        )
        {
            return FailRoute($"[[{RoutesArray}]] entry missing '{DeviceField}'");
        }
        var deviceNameResult = DeviceName.From(deviceRaw);
        if (deviceNameResult is not Result<DeviceName, DeviceError>.Ok deviceNameOk)
        {
            return FailRoute($"invalid route device name: {deviceRaw}");
        }

        return Result.Success<Route, ConfigStoreError>(
            new Route(serverNameOk.Value, endpointOk.Value, deviceNameOk.Value)
        );
    }

    private static string FormatResource(VisaResource resource) =>
        resource switch
        {
            VisaResource.Tcpip t => $"TCPIP{t.Board}::{t.Host}::{t.LanDevice}::INSTR",
            VisaResource.TcpipSocket s => $"TCPIP{s.Board}::{s.Host}::{s.Port}::SOCKET",
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

    private static Result<PoolConfig, ConfigStoreError> ParsePool(TomlTable table)
    {
        var enabled = true;
        if (table.TryGetValue(PoolEnabledField, out var enabledValue))
        {
            if (enabledValue is not bool b)
            {
                return FailPool($"[{PoolTable}].{PoolEnabledField} must be a boolean");
            }
            enabled = b;
        }

        var idle = PoolConfig.Default.IdleTimeout;
        if (table.TryGetValue(PoolIdleTimeoutField, out var idleValue))
        {
            // Accept either "60s" / "500ms" / "1m" string form or an integer
            // number of seconds. The string form is canonical when serialised.
            if (idleValue is string idleString)
            {
                var parsed = ParseDurationString(idleString);
                if (parsed is null)
                {
                    return FailPool(
                        $"[{PoolTable}].{PoolIdleTimeoutField}: cannot parse duration '{idleString}'"
                    );
                }
                idle = parsed.Value;
            }
            else if (idleValue is long idleLong)
            {
                idle = TimeSpan.FromSeconds(idleLong);
            }
            else
            {
                return FailPool(
                    $"[{PoolTable}].{PoolIdleTimeoutField} must be a duration string (e.g. \"60s\") or integer seconds"
                );
            }
        }

        var maxDevices = PoolConfig.Default.MaxDevices;
        if (table.TryGetValue(PoolMaxDevicesField, out var maxValue))
        {
            if (maxValue is not long maxLong)
            {
                return FailPool($"[{PoolTable}].{PoolMaxDevicesField} must be an integer");
            }
            maxDevices = (int)maxLong;
        }

        var built = PoolConfig.From(enabled, idle, maxDevices);
        if (built is not Result<PoolConfig, PoolConfigError>.Ok ok)
        {
            var err = ((Result<PoolConfig, PoolConfigError>.Error)built).Err;
            return FailPool(err.Message);
        }
        return Result.Success<PoolConfig, ConfigStoreError>(ok.Value);
    }

    private static TimeSpan? ParseDurationString(string text)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        text = text.Trim();
        if (text.Length == 0)
        {
            return null;
        }
        // Split numeric prefix from unit suffix.
        var i = 0;
        while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.'))
        {
            i++;
        }
        if (i == 0)
        {
            return null;
        }
        if (
            !double.TryParse(
                text.AsSpan(0, i),
                System.Globalization.NumberStyles.Float,
                inv,
                out var n
            )
        )
        {
            return null;
        }
        var unit = text[i..].Trim();
        return unit switch
        {
            "ms" => TimeSpan.FromMilliseconds(n),
            "s" or "" => TimeSpan.FromSeconds(n),
            "m" => TimeSpan.FromMinutes(n),
            "h" => TimeSpan.FromHours(n),
            _ => null,
        };
    }

    private static string FormatDuration(TimeSpan value)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (value.TotalMilliseconds < 1000 && value != TimeSpan.Zero)
        {
            return string.Create(inv, $"{(long)value.TotalMilliseconds}ms");
        }
        if (value.TotalSeconds < 60)
        {
            return string.Create(inv, $"{(long)value.TotalSeconds}s");
        }
        if (value.TotalMinutes < 60)
        {
            return string.Create(inv, $"{(long)value.TotalMinutes}m");
        }
        return string.Create(inv, $"{(long)value.TotalHours}h");
    }

    private static Result<PoolConfig, ConfigStoreError> FailPool(string reason) =>
        Result.Failure<PoolConfig, ConfigStoreError>(new ConfigStoreParseFailure(reason));

    private static Result<ApiConfig, ConfigStoreError> ParseApi(TomlTable table)
    {
        var tls = TlsConfig.Default;
        if (table.TryGetValue(ApiTlsTable, out var tlsValue))
        {
            if (tlsValue is not TomlTable tlsTable)
            {
                return FailApi($"expected [{ApiTable}.{ApiTlsTable}] to be a TOML table");
            }
            var tlsResult = ParseTls(tlsTable);
            if (tlsResult is not Result<TlsConfig, ConfigStoreError>.Ok tlsOk)
            {
                return Result.Failure<ApiConfig, ConfigStoreError>(
                    ((Result<TlsConfig, ConfigStoreError>.Error)tlsResult).Err
                );
            }
            tls = tlsOk.Value;
        }
        return Result.Success<ApiConfig, ConfigStoreError>(new ApiConfig(tls));
    }

    private static Result<TlsConfig, ConfigStoreError> ParseTls(TomlTable table)
    {
        var enabled = ReadBool(table, TlsEnabledField, false);
        var selfSigned = ReadBool(table, TlsSelfSignedField, false);
        var clientRequired = ReadBool(table, TlsClientRequiredField, false);
        var certPath = ReadString(table, TlsCertPathField);
        var keyPath = ReadString(table, TlsKeyPathField);
        var passwordEnv = ReadString(table, TlsPasswordEnvField);
        var clientCaPath = ReadString(table, TlsClientCaPathField);

        var built = TlsConfig.From(
            enabled,
            certPath,
            keyPath,
            passwordEnv,
            selfSigned,
            clientRequired,
            clientCaPath
        );
        if (built is not Result<TlsConfig, TlsConfigError>.Ok ok)
        {
            var err = ((Result<TlsConfig, TlsConfigError>.Error)built).Err;
            return FailTls(err.Message);
        }
        return Result.Success<TlsConfig, ConfigStoreError>(ok.Value);
    }

    private static bool ReadBool(TomlTable table, string field, bool defaultValue) =>
        table.TryGetValue(field, out var value) && value is bool b ? b : defaultValue;

    private static string? ReadString(TomlTable table, string field) =>
        table.TryGetValue(field, out var value) && value is string s ? s : null;

    private static Result<ApiConfig, ConfigStoreError> FailApi(string reason) =>
        Result.Failure<ApiConfig, ConfigStoreError>(new ConfigStoreParseFailure(reason));

    private static Result<TlsConfig, ConfigStoreError> FailTls(string reason) =>
        Result.Failure<TlsConfig, ConfigStoreError>(new ConfigStoreParseFailure(reason));

    private static Result<TelemetryConfig, ConfigStoreError> ParseTelemetry(TomlTable table)
    {
        var enabled = ReadBool(table, TelemetryEnabledField, false);
        var otlpEndpoint = ReadString(table, TelemetryOtlpEndpointField);
        var serviceName = ReadString(table, TelemetryServiceNameField) ?? "ivi-cli";
        var tracesEnabled = ReadBool(table, TelemetryTracesEnabledField, true);
        var metricsEnabled = ReadBool(table, TelemetryMetricsEnabledField, true);

        var built = TelemetryConfig.From(
            enabled,
            otlpEndpoint,
            serviceName,
            tracesEnabled,
            metricsEnabled
        );
        if (built is not Result<TelemetryConfig, TelemetryConfigError>.Ok ok)
        {
            var err = ((Result<TelemetryConfig, TelemetryConfigError>.Error)built).Err;
            return FailTelemetry(err.Message);
        }
        return Result.Success<TelemetryConfig, ConfigStoreError>(ok.Value);
    }

    private static Result<TelemetryConfig, ConfigStoreError> FailTelemetry(string reason) =>
        Result.Failure<TelemetryConfig, ConfigStoreError>(new ConfigStoreParseFailure(reason));

    private static Result<AuditConfig, ConfigStoreError> ParseAudit(TomlTable table)
    {
        var enabled = ReadBool(table, AuditEnabledField, true);
        var path = ReadString(table, AuditPathField);
        var built = AuditConfig.From(enabled, path);
        if (built is not Result<AuditConfig, AuditConfigError>.Ok ok)
        {
            var err = ((Result<AuditConfig, AuditConfigError>.Error)built).Err;
            return Result.Failure<AuditConfig, ConfigStoreError>(
                new ConfigStoreParseFailure(err.Message)
            );
        }
        return Result.Success<AuditConfig, ConfigStoreError>(ok.Value);
    }

    private static Result<PluginsConfig, ConfigStoreError> ParsePlugins(TomlTable table)
    {
        var enabled = ReadBool(table, PluginsEnabledField, false);
        var allowed = System.Collections.Immutable.ImmutableArray<string>.Empty;
        if (table.TryGetValue(PluginsAllowedField, out var allowedValue))
        {
            if (allowedValue is not TomlArray allowedArray)
            {
                return Result.Failure<PluginsConfig, ConfigStoreError>(
                    new ConfigStoreParseFailure(
                        $"expected [{PluginsTable}].{PluginsAllowedField} to be an array"
                    )
                );
            }
            var builder = System.Collections.Immutable.ImmutableArray.CreateBuilder<string>();
            foreach (var entry in allowedArray)
            {
                if (entry is not string s)
                {
                    return Result.Failure<PluginsConfig, ConfigStoreError>(
                        new ConfigStoreParseFailure(
                            $"[{PluginsTable}].{PluginsAllowedField} entries must be strings"
                        )
                    );
                }
                builder.Add(s);
            }
            allowed = builder.ToImmutable();
        }
        var built = PluginsConfig.From(enabled, allowed);
        if (built is not Result<PluginsConfig, PluginsConfigError>.Ok ok)
        {
            var err = ((Result<PluginsConfig, PluginsConfigError>.Error)built).Err;
            return Result.Failure<PluginsConfig, ConfigStoreError>(
                new ConfigStoreParseFailure(err.Message)
            );
        }
        return Result.Success<PluginsConfig, ConfigStoreError>(ok.Value);
    }

    private static Result<ConfigDocument, ConfigStoreError> Fail(string reason) =>
        Result.Failure<ConfigDocument, ConfigStoreError>(new ConfigStoreParseFailure(reason));

    private static Result<Device, ConfigStoreError> FailDevice(string reason) =>
        Result.Failure<Device, ConfigStoreError>(new ConfigStoreParseFailure(reason));

    private static Result<Server, ConfigStoreError> FailServer(string reason) =>
        Result.Failure<Server, ConfigStoreError>(new ConfigStoreParseFailure(reason));

    private static Result<Route, ConfigStoreError> FailRoute(string reason) =>
        Result.Failure<Route, ConfigStoreError>(new ConfigStoreParseFailure(reason));
}
