using IviCli.Application.Configuration;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Servers;
using IviCli.Domain.Visa;
using IviCli.Infrastructure.Configuration;
using IviCli.TestKit;

namespace IviCli.Infrastructure.Tests.Configuration;

public class TomlConfigParserTests
{
    [Fact]
    public void Parse_EmptyDocument_ReturnsEmptyConfig()
    {
        // Given / When
        var result = TomlConfigParser.Parse("");

        // Then
        result.ShouldBeOk().ShouldBe(ConfigDocument.Empty);
    }

    [Fact]
    public void Parse_DocumentWithOneDevice_ReturnsConfigWithDevice()
    {
        // Given
        var toml = """
            [[devices]]
            name = "psu1"
            resource = "TCPIP0::192.168.0.10::inst0::INSTR"
            timeout_ms = 3000
            """;

        // When
        var result = TomlConfigParser.Parse(toml);

        // Then
        var config = result.ShouldBeOk();
        config.Devices.Length.ShouldBe(1);
        config.Devices[0].Name.Value.ShouldBe("psu1");
        config.Devices[0].Resource.ShouldBeOfType<VisaResource.Tcpip>();
        config.Devices[0].Timeout.Milliseconds.ShouldBe(3000);
    }

    [Fact]
    public void Parse_DocumentWithDefaultDevice_LinksDefault()
    {
        // Given
        var toml = """
            [defaults]
            device = "psu1"

            [[devices]]
            name = "psu1"
            resource = "TCPIP0::host::inst0::INSTR"
            timeout_ms = 3000
            """;

        // When
        var result = TomlConfigParser.Parse(toml);

        // Then
        var config = result.ShouldBeOk();
        config.Defaults.Device.ShouldNotBeNull();
        config.Defaults.Device!.Value.ShouldBe("psu1");
    }

    [Fact]
    public void Parse_DefaultDevicePointingToMissingDevice_ReturnsParseFailure()
    {
        // Given
        var toml = """
            [defaults]
            device = "ghost"
            """;

        // When
        var result = TomlConfigParser.Parse(toml);

        // Then
        result.ShouldBeError().ShouldBeOfType<ConfigStoreParseFailure>();
    }

    [Fact]
    public void Parse_DuplicateDeviceName_ReturnsParseFailure()
    {
        // Given
        var toml = """
            [[devices]]
            name = "psu1"
            resource = "TCPIP0::host::inst0::INSTR"
            timeout_ms = 3000

            [[devices]]
            name = "psu1"
            resource = "USB0::0x0699::0x0408::SN::INSTR"
            timeout_ms = 5000
            """;

        // When
        var result = TomlConfigParser.Parse(toml);

        // Then
        result.ShouldBeError().ShouldBeOfType<ConfigStoreParseFailure>();
    }

    [Fact]
    public void Parse_InvalidDeviceName_ReturnsParseFailure()
    {
        // Given
        var toml = """
            [[devices]]
            name = "Invalid Name!"
            resource = "TCPIP0::host::inst0::INSTR"
            timeout_ms = 3000
            """;

        // When
        var result = TomlConfigParser.Parse(toml);

        // Then
        result.ShouldBeError().ShouldBeOfType<ConfigStoreParseFailure>();
    }

    [Fact]
    public void Parse_MalformedToml_ReturnsParseFailure()
    {
        // Given
        const string toml = "[[devices not valid toml at all";

        // When
        var result = TomlConfigParser.Parse(toml);

        // Then
        result.ShouldBeError().ShouldBeOfType<ConfigStoreParseFailure>();
    }

    [Fact]
    public void Serialize_RoundtripsThroughParse()
    {
        // Given
        var original = ConfigDocument
            .Empty.AddDevice(
                new Device(
                    DeviceName.From("psu1").ShouldBeOk(),
                    VisaResource.Parse("TCPIP0::192.168.0.10::inst0::INSTR").ShouldBeOk(),
                    Timeout.FromMilliseconds(3000).ShouldBeOk()
                )
            )
            .ShouldBeOk()
            .AddDevice(
                new Device(
                    DeviceName.From("scope1").ShouldBeOk(),
                    VisaResource.Parse("USB0::0x0699::0x0408::SN1::INSTR").ShouldBeOk(),
                    Timeout.FromMilliseconds(5000).ShouldBeOk()
                )
            )
            .ShouldBeOk()
            .SetDefaultDevice(DeviceName.From("psu1").ShouldBeOk())
            .ShouldBeOk();

        // When
        var serialized = TomlConfigParser.Serialize(original);
        var roundtripped = TomlConfigParser.Parse(serialized).ShouldBeOk();

        // Then
        roundtripped.ShouldBe(original);
    }

    [Fact]
    public void Parse_UsbIpServerType_IsRecognised()
    {
        // Given
        var toml = """
            [[servers]]
            name = "usb-srv"
            type = "usbip"
            bind = "127.0.0.1"
            port = 3240
            """;

        // When
        var config = TomlConfigParser.Parse(toml).ShouldBeOk();

        // Then
        config.Servers.Single().Type.ShouldBe(ServerType.UsbIp);
    }

    [Fact]
    public void Serialize_RoundTrips_UsbIpServerAndItsBusIdRoute()
    {
        // Given: a USB/IP gateway whose route endpoint is a busid, the
        // shape `usbip attach -b` names on the client side (ADR 0049 §4).
        var deviceName = DeviceName.From("dut").ShouldBeOk();
        var serverName = ServerName.From("usb-srv").ShouldBeOk();
        var original = ConfigDocument
            .Empty.AddDevice(
                new Device(
                    deviceName,
                    VisaResource.Parse("TCPIP0::127.0.0.1::5025::SOCKET").ShouldBeOk(),
                    Timeout.FromMilliseconds(3000).ShouldBeOk()
                )
            )
            .ShouldBeOk()
            .AddServer(
                new Server(
                    serverName,
                    ServerType.UsbIp,
                    IpAddress.From("127.0.0.1").ShouldBeOk(),
                    Port.From(3240).ShouldBeOk()
                )
            )
            .ShouldBeOk()
            .AddRoute(new Route(serverName, PublicEndpoint.From("1-1").ShouldBeOk(), deviceName))
            .ShouldBeOk();

        // When
        var roundtripped = TomlConfigParser
            .Parse(TomlConfigParser.Serialize(original))
            .ShouldBeOk();

        // Then
        roundtripped.ShouldBe(original);
        roundtripped.Servers.Single().Type.ShouldBe(ServerType.UsbIp);
        roundtripped.Routes.Single().Endpoint.Value.ShouldBe("1-1");
    }

    [Fact]
    public void Parse_PoolTable_RoundTripsAllFields()
    {
        var toml = """
            [pool]
            enabled = false
            idle_timeout = "30s"
            max_devices = 8
            """;

        var config = TomlConfigParser.Parse(toml).ShouldBeOk();

        config.Pool.Enabled.ShouldBeFalse();
        config.Pool.IdleTimeout.ShouldBe(TimeSpan.FromSeconds(30));
        config.Pool.MaxDevices.ShouldBe(8);
    }

    [Fact]
    public void Parse_MissingPoolTable_DefaultsToPoolConfigDefault()
    {
        var config = TomlConfigParser.Parse("").ShouldBeOk();
        config.Pool.ShouldBe(PoolConfig.Default);
    }

    [Fact]
    public void Parse_NegativePoolIdleTimeout_ReturnsParseFailure()
    {
        var toml = """
            [pool]
            idle_timeout = "-5s"
            """;
        var result = TomlConfigParser.Parse(toml);
        result.ShouldBeError();
    }

    [Fact]
    public void Serialize_RoundTrips_NonDefaultPool()
    {
        var pool = PoolConfig.From(false, TimeSpan.FromSeconds(120), 4).ShouldBeOk();
        var original = ConfigDocument.Empty.WithPool(pool);

        var serialized = TomlConfigParser.Serialize(original);
        var roundtripped = TomlConfigParser.Parse(serialized).ShouldBeOk();

        roundtripped.Pool.ShouldBe(pool);
    }

    [Fact]
    public void Parse_ApiTlsTable_RoundTripsAllFields()
    {
        var toml = """
            [api.tls]
            enabled = true
            cert_path = "/etc/ivi/cert.pfx"
            password_env = "IVI_TLS_PASS"
            client_required = true
            client_ca_path = "/etc/ivi/clients.crt"
            """;

        var config = TomlConfigParser.Parse(toml).ShouldBeOk();

        config.Api.Tls.Enabled.ShouldBeTrue();
        config.Api.Tls.CertPath.ShouldBe("/etc/ivi/cert.pfx");
        config.Api.Tls.PasswordEnv.ShouldBe("IVI_TLS_PASS");
        config.Api.Tls.ClientRequired.ShouldBeTrue();
        config.Api.Tls.ClientCaPath.ShouldBe("/etc/ivi/clients.crt");
    }

    [Fact]
    public void Parse_MissingApiTable_DefaultsToApiDefault()
    {
        var config = TomlConfigParser.Parse("").ShouldBeOk();
        config.Api.ShouldBe(ApiConfig.Default);
    }

    [Fact]
    public void Parse_TlsEnabledWithoutCertOrSelfSigned_Fails()
    {
        var toml = """
            [api.tls]
            enabled = true
            """;
        var result = TomlConfigParser.Parse(toml);
        result.ShouldBeError();
    }

    [Fact]
    public void Serialize_RoundTrips_NonDefaultApiTls()
    {
        var tls = TlsConfig
            .From(true, "/etc/ivi/cert.pfx", null, "IVI_TLS_PASS", false, false, null)
            .ShouldBeOk();
        var original = ConfigDocument.Empty.WithApi(new ApiConfig(tls));

        var serialized = TomlConfigParser.Serialize(original);
        var roundtripped = TomlConfigParser.Parse(serialized).ShouldBeOk();

        roundtripped.Api.Tls.ShouldBe(tls);
    }

    [Fact]
    public void Parse_TelemetryTable_RoundTripsAllFields()
    {
        var toml = """
            [telemetry]
            enabled = true
            otlp_endpoint = "http://otel-collector:4317"
            service_name = "ivi-cli-lab"
            traces_enabled = true
            metrics_enabled = false
            """;
        var cfg = TomlConfigParser.Parse(toml).ShouldBeOk();
        cfg.Telemetry.Enabled.ShouldBeTrue();
        cfg.Telemetry.OtlpEndpoint.ShouldBe("http://otel-collector:4317");
        cfg.Telemetry.ServiceName.ShouldBe("ivi-cli-lab");
        cfg.Telemetry.TracesEnabled.ShouldBeTrue();
        cfg.Telemetry.MetricsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void Parse_MissingTelemetryTable_DefaultsToTelemetryDefault()
    {
        var cfg = TomlConfigParser.Parse("").ShouldBeOk();
        cfg.Telemetry.ShouldBe(TelemetryConfig.Default);
    }

    [Fact]
    public void Parse_InvalidOtlpEndpoint_Fails()
    {
        var toml = """
            [telemetry]
            enabled = true
            otlp_endpoint = "not a uri"
            """;
        TomlConfigParser.Parse(toml).ShouldBeError();
    }

    [Fact]
    public void Serialize_RoundTrips_NonDefaultTelemetry()
    {
        var t = TelemetryConfig
            .From(true, "http://otel:4317", "ivi-cli-lab", true, true)
            .ShouldBeOk();
        var original = ConfigDocument.Empty.WithTelemetry(t);

        var serialized = TomlConfigParser.Serialize(original);
        var rt = TomlConfigParser.Parse(serialized).ShouldBeOk();
        rt.Telemetry.ShouldBe(t);
    }

    [Fact]
    public void Parse_AuditTable_RoundTrips_Fields()
    {
        var toml = """
            [audit]
            enabled = false
            path = "/var/log/ivi/audit.ndjson"
            """;
        var cfg = TomlConfigParser.Parse(toml).ShouldBeOk();
        cfg.Audit.Enabled.ShouldBeFalse();
        cfg.Audit.Path.ShouldBe("/var/log/ivi/audit.ndjson");
    }

    [Fact]
    public void Parse_MissingAuditTable_DefaultsToAuditDefault()
    {
        var cfg = TomlConfigParser.Parse("").ShouldBeOk();
        cfg.Audit.ShouldBe(AuditConfig.Default);
    }

    [Fact]
    public void Serialize_RoundTrips_NonDefaultAudit()
    {
        var a = AuditConfig.From(false, "/var/log/ivi.ndjson").ShouldBeOk();
        var original = ConfigDocument.Empty.WithAudit(a);
        var serialized = TomlConfigParser.Serialize(original);
        var rt = TomlConfigParser.Parse(serialized).ShouldBeOk();
        rt.Audit.ShouldBe(a);
    }

    [Fact]
    public void Parse_PluginsTable_RoundTrips_Fields()
    {
        var toml = """
            [plugins]
            enabled = true
            allowed = ["acme", "vendor-x"]
            """;
        var cfg = TomlConfigParser.Parse(toml).ShouldBeOk();
        cfg.Plugins.Enabled.ShouldBeTrue();
        cfg.Plugins.Allowed.Length.ShouldBe(2);
        cfg.Plugins.Allowed.ShouldContain("acme");
        cfg.Plugins.Allowed.ShouldContain("vendor-x");
    }

    [Fact]
    public void Parse_MissingPluginsTable_DefaultsToDisabled()
    {
        var cfg = TomlConfigParser.Parse("").ShouldBeOk();
        cfg.Plugins.ShouldBe(PluginsConfig.Default);
    }

    [Fact]
    public void Serialize_RoundTrips_NonDefaultPlugins()
    {
        var p = PluginsConfig
            .From(true, System.Collections.Immutable.ImmutableArray.Create("acme"))
            .ShouldBeOk();
        var original = ConfigDocument.Empty.WithPlugins(p);
        var serialized = TomlConfigParser.Serialize(original);
        var rt = TomlConfigParser.Parse(serialized).ShouldBeOk();
        rt.Plugins.ShouldBe(p);
    }
}
