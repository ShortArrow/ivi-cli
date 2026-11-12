using System.CommandLine;

namespace IviCli.Cli.Commands;

/// <summary>
/// Builds the <c>ivicli mock ...</c> command group: the three nouns a mock is
/// made of — scenario, scene, rule — plus the capture reader and
/// <c>serve</c>, which makes a scenario reachable at an address.
/// </summary>
public static class MockCommand
{
    /// <summary>Returns the full <c>mock</c> command, ready to attach to the root.</summary>
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("mock", "Manage mock-device behaviour for the Fake Backend.");
        command.Subcommands.Add(MockScenarioCommand.Build(services));
        command.Subcommands.Add(MockSceneCommand.Build(services));
        command.Subcommands.Add(MockRuleCommand.Build(services));
        command.Subcommands.Add(MockReceivedWritesCommand.Build(services));
        command.Subcommands.Add(MockServeCommand.Build(services));
        return command;
    }
}
