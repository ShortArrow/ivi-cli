using System.CommandLine;
using IviCli.Cli;
using Shouldly;

namespace IviCli.Cli.Tests;

/// <summary>
/// Which invocations pay for scenario activation. Activation reads the
/// scenario store and warns about bindings it cannot load, so an invocation
/// that never opens a session must not run it — a warning line ahead of
/// <c>--version</c> breaks anything parsing that output.
/// </summary>
public sealed class ScenarioActivationTests
{
    private static ParseResult Parse(string commandLine)
    {
        var root = new RootCommand("ivi-cli test root");
        var visa = new Command("visa", "VISA transport / SCPI operations.");
        var list = new Command("list", "List registered devices.");
        list.SetAction(_ => 0);
        visa.Subcommands.Add(list);
        root.Subcommands.Add(visa);
        return root.Parse(commandLine);
    }

    [Theory]
    [InlineData("--version")]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("visa --help")]
    [InlineData("visa list --help")]
    // A group with no subcommand named prints its own help, so it belongs
    // here rather than with the commands that run.
    [InlineData("visa")]
    public void An_invocation_that_only_prints_metadata_activates_nothing(string commandLine)
    {
        // Given / When / Then
        ScenarioActivation.IsNeededFor(Parse(commandLine)).ShouldBeFalse();
    }

    [Fact]
    public void An_invocation_that_can_reach_a_backend_activates()
    {
        // Given / When / Then
        ScenarioActivation.IsNeededFor(Parse("visa list")).ShouldBeTrue();
    }

    [Fact]
    public void A_command_line_that_does_not_parse_activates_nothing()
    {
        // Given a token the root command has no symbol for
        var parseResult = Parse("visa list --no-such-option");

        // When / Then — the run ends in a parse error, so the scenario
        // store is never needed
        parseResult.Errors.ShouldNotBeEmpty();
        ScenarioActivation.IsNeededFor(parseResult).ShouldBeFalse();
    }

    [Fact]
    public void An_argument_that_merely_looks_like_help_still_activates()
    {
        // Given a command taking a free-form value
        var root = new RootCommand("ivi-cli test root");
        var query = new Command("query", "Send a SCPI query.");
        var scpi = new Argument<string>("scpi");
        query.Arguments.Add(scpi);
        query.SetAction(_ => 0);
        root.Subcommands.Add(query);

        // When the value happens to spell an option the parser knows
        var parseResult = root.Parse(["query", "--", "--version"]);

        // Then the decision follows what will run, not the raw tokens
        ScenarioActivation.IsNeededFor(parseResult).ShouldBeTrue();
    }
}
