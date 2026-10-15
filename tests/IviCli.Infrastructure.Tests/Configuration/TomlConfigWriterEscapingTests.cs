using IviCli.Infrastructure.Configuration;
using IviCli.TestKit;

namespace IviCli.Infrastructure.Tests.Configuration;

/// <summary>
/// Every string the writer emits survives a round trip, including Windows
/// paths, whose backslashes a TOML basic string must escape.
/// </summary>
public class TomlConfigWriterEscapingTests
{
    [Fact]
    public void Serialize_WindowsPathsAndQuotes_RoundTrip()
    {
        // Given: TOML literal strings, which keep backslashes as written.
        var toml = """
            [api.tls]
            enabled = true
            cert_path = 'C:\ivi\certs\gateway.pfx'
            key_path = 'C:\ivi\certs\gateway.key'
            password_env = 'IVI_TLS_PASS'
            client_required = true
            client_ca_path = 'C:\ivi\certs\clients.crt'

            [telemetry]
            enabled = true
            otlp_endpoint = 'http://collector:4317'
            service_name = 'lab "bench" 2'

            [audit]
            enabled = true
            path = 'C:\ProgramData\ivi\audit.ndjson'

            [plugins]
            enabled = true
            allowed = ['acme', 'vendor "x"']
            """;
        var config = TomlConfigParser.Parse(toml).ShouldBeOk();

        // When
        var written = TomlConfigParser.Serialize(config);

        // Then
        TomlConfigParser.Parse(written).ShouldBeOk().ShouldBe(config);
    }
}
