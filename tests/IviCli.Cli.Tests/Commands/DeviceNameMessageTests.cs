using IviCli.Cli.Commands;
using Shouldly;

namespace IviCli.Cli.Tests.Commands;

public sealed class DeviceNameMessageTests
{
    [Fact]
    public void States_the_rule_and_offers_a_conforming_alternative()
    {
        // Given / When
        var message = DeviceNameMessage.Invalid("PSU-1");

        // Then
        message.ShouldBe(
            "error: invalid device name 'PSU-1': use lowercase letters, digits, underscores "
                + "and hyphens, starting with a letter, at most 64 characters. Try 'psu-1'."
        );
    }

    [Fact]
    public void Offers_nothing_when_folding_cannot_reach_a_valid_name()
    {
        // Given / When
        var message = DeviceNameMessage.Invalid("1psu");

        // Then
        message.ShouldStartWith("error: invalid device name '1psu': use ");
        message.ShouldNotContain("Try ");
    }
}
