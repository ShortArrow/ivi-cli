using System.CommandLine;
using IviCli.Cli.Commands;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace IviCli.Cli.Tests;

/// <summary>
/// The logging flags the README documents are read before the parser runs,
/// and the parser must accept them too, before or after any subcommand;
/// otherwise every command given one of them exits with "unrecognized".
/// </summary>
public sealed class GlobalLoggingOptionsTests
{
    private static ParseResult Parse(string commandLine)
    {
        var root = new RootCommand("test root");
        GlobalLoggingOptions.AddTo(root);
        root.Subcommands.Add(ServerCommand.Build(new ServiceCollection().BuildServiceProvider()));
        return root.Parse(commandLine);
    }

    [Theory]
    [InlineData("-v server start gw28")]
    [InlineData("server start gw28 -v")]
    [InlineData("--verbose server start gw28")]
    [InlineData("-vv server start gw28")]
    [InlineData("-q server start gw28")]
    [InlineData("--quiet server start gw28")]
    [InlineData("--log-format json server start gw28")]
    [InlineData("server start gw28 --log-file C:/tmp/gw.log")]
    public void A_documented_logging_flag_parses_with_any_command(string commandLine) =>
        Parse(commandLine).Errors.ShouldBeEmpty();
}
