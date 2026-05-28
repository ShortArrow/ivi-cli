using IviCli.Domain.Configuration;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Domain.Tests.Configuration;

public sealed class AuditConfigTests
{
    [Fact]
    public void Default_is_enabled_with_no_path_override()
    {
        AuditConfig.Default.Enabled.ShouldBeTrue();
        AuditConfig.Default.Path.ShouldBeNull();
    }

    [Fact]
    public void From_accepts_explicit_path()
    {
        var cfg = AuditConfig.From(true, "/var/log/ivi/audit.ndjson").ShouldBeOk();
        cfg.Path.ShouldBe("/var/log/ivi/audit.ndjson");
    }

    [Fact]
    public void From_treats_null_path_as_default()
    {
        var cfg = AuditConfig.From(false, null).ShouldBeOk();
        cfg.Path.ShouldBeNull();
    }

    [Fact]
    public void From_blank_whitespace_path_fails()
    {
        var result = AuditConfig.From(true, "   ");
        result.ShouldBeError().ShouldBeOfType<AuditPathBlank>();
    }

    [Fact]
    public void Equality_is_structural()
    {
        var a = AuditConfig.From(true, "/p").ShouldBeOk();
        var b = AuditConfig.From(true, "/p").ShouldBeOk();
        var c = AuditConfig.From(false, "/p").ShouldBeOk();
        a.ShouldBe(b);
        a.ShouldNotBe(c);
    }
}
