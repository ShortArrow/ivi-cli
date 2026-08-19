using System.CommandLine;
using IviCli.Cli.Commands;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace IviCli.Cli.Tests.Commands;

public sealed class MockCommandTreeTests
{
    private static Command BuildMock() =>
        MockCommand.Build(new ServiceCollection().BuildServiceProvider());

    private static ParseResult Parse(string commandLine)
    {
        var root = new RootCommand("test root");
        root.Subcommands.Add(BuildMock());
        return root.Parse(commandLine);
    }

    [Theory]
    [InlineData("scenario")]
    [InlineData("scene")]
    [InlineData("rule")]
    [InlineData("received")]
    public void Every_noun_a_mock_is_made_of_shows_under_mock(string name)
    {
        // Given / When
        var noun = BuildMock().Subcommands.SingleOrDefault(c => c.Name == name);

        // Then
        noun.ShouldNotBeNull();
        noun.Hidden.ShouldBeFalse();
    }

    [Theory]
    [InlineData("mock scene add my-dmm idle")]
    [InlineData("mock rule add my-dmm --in idle --match *IDN? --ack")]
    public void The_flat_spelling_parses(string commandLine)
    {
        // Given / When / Then
        Parse(commandLine).Errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("mock scenario scene add my-dmm idle")]
    [InlineData("mock scenario rule add my-dmm --in idle --match *IDN? --ack")]
    public void The_nested_spelling_keeps_working_until_it_is_removed(string commandLine)
    {
        // Given / When / Then
        Parse(commandLine).Errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("scene")]
    [InlineData("rule")]
    public void The_nested_spelling_is_hidden_from_help(string name)
    {
        // Given
        var scenario = BuildMock().Subcommands.Single(c => c.Name == "scenario");

        // When / Then
        scenario.Subcommands.Single(c => c.Name == name).Hidden.ShouldBeTrue();
    }
}
