using IviCli.Application.Audit;
using Shouldly;

namespace IviCli.Application.Tests.Audit;

/// <summary>
/// Locks in the Subject field on <see cref="ConfigMutated"/> and
/// <see cref="ServerLifecycle"/> introduced by Batch U: the field is
/// optional positional so legacy 3-arg callers keep compiling, and
/// defaults to null when omitted.
/// </summary>
public sealed class AuditEventTests
{
    private static readonly DateTimeOffset Instant = new(2026, 5, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConfigMutated_default_subject_is_null()
    {
        var ev = new ConfigMutated(Instant, "device.add", "psu1");
        ev.Subject.ShouldBeNull();
    }

    [Fact]
    public void ConfigMutated_explicit_subject_round_trips()
    {
        var ev = new ConfigMutated(Instant, "device.add", "psu1", "cli/alice");
        ev.Subject.ShouldBe("cli/alice");
        ev.Kind.ShouldBe("config.mutated");
    }

    [Fact]
    public void ServerLifecycle_default_subject_is_null()
    {
        var ev = new ServerLifecycle(Instant, "hslip-1", "start");
        ev.Subject.ShouldBeNull();
    }

    [Fact]
    public void ServerLifecycle_explicit_subject_round_trips()
    {
        var ev = new ServerLifecycle(Instant, "hslip-1", "crashed", "cli/bob");
        ev.Subject.ShouldBe("cli/bob");
        ev.Action.ShouldBe("crashed");
        ev.Kind.ShouldBe("server.lifecycle");
    }
}
