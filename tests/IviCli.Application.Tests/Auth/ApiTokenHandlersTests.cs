using IviCli.Application.Auth;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Application.Tests.Auth;

public sealed class ApiTokenHandlersTests
{
    [Fact]
    public async Task Create_mints_token_with_ivicli_pat_prefix_and_persists_hash()
    {
        var store = new FakeApiTokenStore();
        var handler = new CreateApiTokenCommandHandler(
            store,
            () => new DateTimeOffset(2026, 5, 27, 12, 0, 0, TimeSpan.Zero)
        );

        var report = (
            await handler.HandleAsync(
                new CreateApiTokenCommand("lab dashboard"),
                TestContext.Current.CancellationToken
            )
        ).ShouldBeOk();

        report.Token.ShouldStartWith("ivicli_pat_");
        report.Stored.Label.ShouldBe("lab dashboard");
        report.Stored.Id.Length.ShouldBe(6);
        report.Stored.HashHex.Length.ShouldBe(64); // SHA-256 hex
        report.Stored.LastUsedAt.ShouldBeNull();
        store.Current.Tokens.Length.ShouldBe(1);
        // The raw token must NOT round-trip — only the hash is stored.
        store
            .Current.Tokens[0]
            .HashHex.ShouldBe(CreateApiTokenCommandHandler.HashHex(report.Token));
    }

    [Fact]
    public async Task Create_with_empty_label_persists_empty_string()
    {
        var store = new FakeApiTokenStore();
        var handler = new CreateApiTokenCommandHandler(store);
        var report = (
            await handler.HandleAsync(
                new CreateApiTokenCommand(""),
                TestContext.Current.CancellationToken
            )
        ).ShouldBeOk();

        report.Stored.Label.ShouldBe("");
    }

    [Fact]
    public async Task Create_truncates_label_to_64_chars()
    {
        var store = new FakeApiTokenStore();
        var handler = new CreateApiTokenCommandHandler(store);
        var report = (
            await handler.HandleAsync(
                new CreateApiTokenCommand(new string('x', 100)),
                TestContext.Current.CancellationToken
            )
        ).ShouldBeOk();

        report.Stored.Label.Length.ShouldBe(64);
    }

    [Fact]
    public async Task Create_surfaces_store_save_failure()
    {
        var store = new FakeApiTokenStore();
        store.FailNextSaveWith("disk full");
        var handler = new CreateApiTokenCommandHandler(store);

        var result = await handler.HandleAsync(
            new CreateApiTokenCommand("x"),
            TestContext.Current.CancellationToken
        );

        result.ShouldBeError().ShouldBeOfType<ApiTokenStoreWriteFailure>();
    }

    [Fact]
    public async Task List_returns_all_tokens_from_store()
    {
        var store = new FakeApiTokenStore();
        await new CreateApiTokenCommandHandler(store).HandleAsync(
            new CreateApiTokenCommand("a"),
            TestContext.Current.CancellationToken
        );
        await new CreateApiTokenCommandHandler(store).HandleAsync(
            new CreateApiTokenCommand("b"),
            TestContext.Current.CancellationToken
        );

        var listing = (
            await new ListApiTokensQueryHandler(store).HandleAsync(
                new ListApiTokensQuery(),
                TestContext.Current.CancellationToken
            )
        ).ShouldBeOk();

        listing.Length.ShouldBe(2);
        listing.Select(t => t.Label).ShouldBe(["a", "b"]);
    }

    [Fact]
    public async Task Revoke_removes_token_by_id()
    {
        var store = new FakeApiTokenStore();
        var created = (
            await new CreateApiTokenCommandHandler(store).HandleAsync(
                new CreateApiTokenCommand("victim"),
                TestContext.Current.CancellationToken
            )
        ).ShouldBeOk();
        var handler = new RevokeApiTokenCommandHandler(store);

        var revoked = (
            await handler.HandleAsync(
                new RevokeApiTokenCommand(created.Stored.Id),
                TestContext.Current.CancellationToken
            )
        ).ShouldBeOk();

        revoked.Id.ShouldBe(created.Stored.Id);
        store.Current.Tokens.ShouldBeEmpty();
    }

    [Fact]
    public async Task Revoke_unknown_id_returns_RevokeApiTokenUnknown()
    {
        var store = new FakeApiTokenStore();
        var handler = new RevokeApiTokenCommandHandler(store);

        var result = await handler.HandleAsync(
            new RevokeApiTokenCommand("ffffff"),
            TestContext.Current.CancellationToken
        );

        result.ShouldBeError().ShouldBeOfType<RevokeApiTokenUnknown>();
    }
}
