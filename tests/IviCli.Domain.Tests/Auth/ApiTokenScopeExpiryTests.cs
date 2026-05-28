using System.Collections.Immutable;
using IviCli.Domain.Auth;
using Shouldly;

namespace IviCli.Domain.Tests.Auth;

public sealed class ApiTokenScopeExpiryTests
{
    private static ApiToken Build(
        ImmutableArray<string> scopes = default,
        DateTimeOffset? expiresAt = null
    ) =>
        new(
            Id: "abc",
            HashHex: "deadbeef",
            Label: "test",
            CreatedAt: DateTimeOffset.UtcNow,
            LastUsedAt: null,
            Scopes: scopes,
            ExpiresAt: expiresAt
        );

    [Fact]
    public void HasScope_empty_scopes_grants_anything_backward_compat()
    {
        var t = Build();
        t.HasScope("read:devices").ShouldBeTrue();
        t.HasScope("write:scpi").ShouldBeTrue();
        t.HasScope("admin:tokens").ShouldBeTrue();
    }

    [Fact]
    public void HasScope_explicit_list_only_matches_listed()
    {
        var t = Build(ImmutableArray.Create("read:devices", "read:servers"));
        t.HasScope("read:devices").ShouldBeTrue();
        t.HasScope("read:servers").ShouldBeTrue();
        t.HasScope("write:scpi").ShouldBeFalse();
    }

    [Fact]
    public void IsExpired_null_ExpiresAt_never_expires()
    {
        var t = Build();
        t.IsExpired(DateTimeOffset.UtcNow.AddYears(100)).ShouldBeFalse();
    }

    [Fact]
    public void IsExpired_returns_true_when_now_past_ExpiresAt()
    {
        var exp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var t = Build(expiresAt: exp);
        t.IsExpired(exp.AddSeconds(1)).ShouldBeTrue();
        t.IsExpired(exp.AddSeconds(-1)).ShouldBeFalse();
        t.IsExpired(exp).ShouldBeFalse(); // strictly past
    }
}
