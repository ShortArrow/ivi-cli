using System.Security.Cryptography.X509Certificates;
using IviCli.Api.Tls;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Api.Tests.Tls;

public sealed class TlsCertificateLoaderTests
{
    [Fact]
    public void Load_disabled_config_returns_TlsLoadDisabled()
    {
        var result = TlsCertificateLoader.Load(TlsConfig.Default);
        result.ShouldBeError().ShouldBeOfType<TlsLoadDisabled>();
    }

    [Fact]
    public void Load_self_signed_returns_a_valid_24h_cert()
    {
        var cfg = TlsConfig
            .From(true, null, null, null, selfSigned: true, false, null)
            .ShouldBeOk();

        using var bundleCert = TlsCertificateLoader.Load(cfg).ShouldBeOk().ServerCertificate;

        bundleCert.HasPrivateKey.ShouldBeTrue();
        bundleCert.NotAfter.ShouldBeGreaterThan(DateTime.Now);
        bundleCert.NotAfter.ShouldBeLessThan(DateTime.Now.AddDays(2));
        bundleCert.Subject.ShouldContain("ivi-cli-dev");
    }

    [Fact]
    public void Load_cert_path_missing_returns_TlsCertFileMissing()
    {
        var cfg = TlsConfig
            .From(true, "/definitely/does/not/exist.pfx", null, null, false, false, null)
            .ShouldBeOk();
        var result = TlsCertificateLoader.Load(cfg);
        result.ShouldBeError().ShouldBeOfType<TlsCertFileMissing>();
    }

    [Fact]
    public void GenerateSelfSigned_includes_localhost_san()
    {
        using var cert = TlsCertificateLoader.GenerateSelfSigned();

        var sanExt = cert.Extensions["2.5.29.17"];
        sanExt.ShouldNotBeNull();
        sanExt.Format(false).ShouldContain("localhost");
    }
}
