using IviCli.Domain.Configuration;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Domain.Tests.Configuration;

public sealed class PoolConfigTests
{
    [Fact]
    public void Default_is_enabled_with_60s_idle_and_16_max()
    {
        PoolConfig.Default.Enabled.ShouldBeTrue();
        PoolConfig.Default.IdleTimeout.ShouldBe(TimeSpan.FromSeconds(60));
        PoolConfig.Default.MaxDevices.ShouldBe(16);
    }

    [Fact]
    public void From_rejects_negative_idle_timeout()
    {
        var result = PoolConfig.From(true, TimeSpan.FromSeconds(-1), 10);
        result.ShouldBeError().ShouldBeOfType<NegativeIdleTimeout>();
    }

    [Fact]
    public void From_rejects_negative_max_devices()
    {
        var result = PoolConfig.From(true, TimeSpan.FromSeconds(60), -1);
        result.ShouldBeError().ShouldBeOfType<NegativeMaxDevices>();
    }

    [Fact]
    public void Equality_is_structural()
    {
        var a = PoolConfig.From(true, TimeSpan.FromSeconds(30), 8).ShouldBeOk();
        var b = PoolConfig.From(true, TimeSpan.FromSeconds(30), 8).ShouldBeOk();
        var c = PoolConfig.From(true, TimeSpan.FromSeconds(30), 9).ShouldBeOk();
        a.ShouldBe(b);
        a.ShouldNotBe(c);
    }
}
