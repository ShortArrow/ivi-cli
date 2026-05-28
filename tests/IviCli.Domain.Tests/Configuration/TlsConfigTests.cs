using IviCli.Domain.Configuration;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Domain.Tests.Configuration;

public sealed class TlsConfigTests
{
    [Fact]
    public void Default_is_disabled_with_no_paths()
    {
        TlsConfig.Default.Enabled.ShouldBeFalse();
        TlsConfig.Default.CertPath.ShouldBeNull();
        TlsConfig.Default.SelfSigned.ShouldBeFalse();
        TlsConfig.Default.ClientRequired.ShouldBeFalse();
    }

    [Fact]
    public void From_enabled_with_cert_path_succeeds()
    {
        var result = TlsConfig.From(
            enabled: true,
            certPath: "/etc/ivi/cert.pfx",
            keyPath: null,
            passwordEnv: "IVI_TLS_PASS",
            selfSigned: false,
            clientRequired: false,
            clientCaPath: null
        );
        var cfg = result.ShouldBeOk();
        cfg.Enabled.ShouldBeTrue();
        cfg.CertPath.ShouldBe("/etc/ivi/cert.pfx");
        cfg.PasswordEnv.ShouldBe("IVI_TLS_PASS");
    }

    [Fact]
    public void From_enabled_with_self_signed_succeeds()
    {
        var result = TlsConfig.From(
            enabled: true,
            certPath: null,
            keyPath: null,
            passwordEnv: null,
            selfSigned: true,
            clientRequired: false,
            clientCaPath: null
        );
        var cfg = result.ShouldBeOk();
        cfg.SelfSigned.ShouldBeTrue();
    }

    [Fact]
    public void From_enabled_with_neither_cert_nor_selfSigned_fails()
    {
        var result = TlsConfig.From(true, null, null, null, false, false, null);
        result.ShouldBeError().ShouldBeOfType<TlsCertSourceAmbiguous>();
    }

    [Fact]
    public void From_enabled_with_both_cert_and_selfSigned_fails()
    {
        var result = TlsConfig.From(true, "/x.pfx", null, null, selfSigned: true, false, null);
        result.ShouldBeError().ShouldBeOfType<TlsCertSourceAmbiguous>();
    }

    [Fact]
    public void From_disabled_with_options_set_fails()
    {
        var result = TlsConfig.From(false, "/x.pfx", null, null, false, false, null);
        result.ShouldBeError().ShouldBeOfType<TlsDisabledButOptionsSet>();
    }

    [Fact]
    public void From_client_required_without_ca_path_fails()
    {
        var result = TlsConfig.From(
            enabled: true,
            certPath: "/cert.pfx",
            keyPath: null,
            passwordEnv: null,
            selfSigned: false,
            clientRequired: true,
            clientCaPath: null
        );
        result.ShouldBeError().ShouldBeOfType<TlsClientCaMissing>();
    }

    [Fact]
    public void From_client_required_with_ca_path_succeeds()
    {
        var result = TlsConfig.From(
            enabled: true,
            certPath: "/cert.pfx",
            keyPath: null,
            passwordEnv: null,
            selfSigned: false,
            clientRequired: true,
            clientCaPath: "/clients.crt"
        );
        var cfg = result.ShouldBeOk();
        cfg.ClientRequired.ShouldBeTrue();
        cfg.ClientCaPath.ShouldBe("/clients.crt");
    }
}
