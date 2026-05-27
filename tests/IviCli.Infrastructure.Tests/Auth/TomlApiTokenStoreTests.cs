using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using IviCli.Domain.Auth;
using IviCli.Infrastructure.Auth;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Infrastructure.Tests.Auth;

public sealed class TomlApiTokenStoreTests
{
    private const string Path = "/var/lib/ivi-cli/auth/api-tokens.toml";

    [Fact]
    public async Task LoadAsync_returns_Empty_when_file_does_not_exist()
    {
        var fs = new MockFileSystem();
        var store = new TomlApiTokenStore(fs, Path);

        var result = await store.LoadAsync(default);

        result.ShouldBeOk().ShouldBe(ApiTokenDocument.Empty);
    }

    [Fact]
    public async Task Round_trip_preserves_all_token_fields()
    {
        var fs = new MockFileSystem();
        var store = new TomlApiTokenStore(fs, Path);
        var token = new ApiToken(
            Id: "4f9c2a",
            HashHex: "deadbeef1234deadbeef1234deadbeef1234deadbeef1234deadbeef12341234",
            Label: "lab dashboard",
            CreatedAt: new DateTimeOffset(2026, 5, 27, 12, 0, 0, TimeSpan.Zero),
            LastUsedAt: new DateTimeOffset(2026, 5, 27, 12, 34, 0, TimeSpan.Zero)
        );
        var doc = new ApiTokenDocument(ImmutableArray.Create(token));

        (await store.SaveAsync(doc, default)).ShouldBeOk();
        var loaded = (await store.LoadAsync(default)).ShouldBeOk();

        loaded.Tokens.Length.ShouldBe(1);
        var rt = loaded.Tokens[0];
        rt.Id.ShouldBe(token.Id);
        rt.HashHex.ShouldBe(token.HashHex);
        rt.Label.ShouldBe(token.Label);
        rt.CreatedAt.ShouldBe(token.CreatedAt);
        rt.LastUsedAt.ShouldBe(token.LastUsedAt);
    }

    [Fact]
    public async Task SaveAsync_creates_parent_directory_if_missing()
    {
        var fs = new MockFileSystem();
        var store = new TomlApiTokenStore(fs, Path);

        (
            await store.SaveAsync(
                new ApiTokenDocument(
                    ImmutableArray.Create(
                        new ApiToken("abc123", "abc", "label", DateTimeOffset.UnixEpoch, null)
                    )
                ),
                default
            )
        ).ShouldBeOk();

        fs.File.Exists(Path).ShouldBeTrue();
    }

    [Fact]
    public async Task LoadAsync_after_save_with_null_lastUsedAt_returns_null()
    {
        var fs = new MockFileSystem();
        var store = new TomlApiTokenStore(fs, Path);
        var token = new ApiToken(
            "abc123",
            "hash",
            "no last use",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            LastUsedAt: null
        );
        await store.SaveAsync(new ApiTokenDocument(ImmutableArray.Create(token)), default);

        var loaded = (await store.LoadAsync(default)).ShouldBeOk();

        loaded.Tokens[0].LastUsedAt.ShouldBeNull();
    }
}
