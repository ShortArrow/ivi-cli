using IviCli.Cli.Commands;
using Shouldly;

namespace IviCli.Cli.Tests.Commands;

public sealed class ApiTokenCommandParseExpiresTests
{
    [Theory]
    [InlineData("30s", 30)]
    [InlineData("5m", 5 * 60)]
    [InlineData("12h", 12 * 60 * 60)]
    [InlineData("7d", 7 * 24 * 60 * 60)]
    public void Duration_shortcuts_add_to_now(string raw, int expectedSeconds)
    {
        var before = DateTimeOffset.UtcNow;
        var result = ApiTokenCommand.ParseExpiresAt(raw);
        var after = DateTimeOffset.UtcNow;

        result.ShouldNotBeNull();
        var diff = (result!.Value - before).TotalSeconds;
        diff.ShouldBeGreaterThanOrEqualTo(expectedSeconds - 1);
        diff.ShouldBeLessThanOrEqualTo(expectedSeconds + (after - before).TotalSeconds + 1);
    }

    [Fact]
    public void Iso8601_absolute_instant_is_parsed_directly()
    {
        var result = ApiTokenCommand.ParseExpiresAt("2027-01-01T00:00:00Z");
        result.ShouldBe(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("0d")]
    [InlineData("-1h")]
    [InlineData("5x")]
    public void Malformed_input_returns_null(string raw)
    {
        ApiTokenCommand.ParseExpiresAt(raw).ShouldBeNull();
    }
}
