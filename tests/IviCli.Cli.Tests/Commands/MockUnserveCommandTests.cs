using System.CommandLine;
using IviCli.Cli.Commands;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace IviCli.Cli.Tests.Commands;

/// <summary><c>mock unserve</c> takes exactly the name to remove (ADR 0056 §7).</summary>
public sealed class MockUnserveCommandTests
{
    private static ParseResult Parse(string commandLine)
    {
        var root = new RootCommand("test root");
        root.Subcommands.Add(MockCommand.Build(new ServiceCollection().BuildServiceProvider()));
        return root.Parse(commandLine);
    }

    [Theory]
    [InlineData("mock unserve mock-socket-5025", true)]
    [InlineData("mock unserve", false)]
    [InlineData("mock unserve a b", false)]
    public void Unserve_takes_one_name(string commandLine, bool parses)
    {
        (Parse(commandLine).Errors.Count == 0).ShouldBe(parses);
    }
}
