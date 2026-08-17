using System.CommandLine;
using IviCli.Cli.Commands;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace IviCli.Cli.Tests.Commands;

public sealed class DoctorCommandTests
{
    private static Command BuildDoctor() =>
        DoctorCommand.Build(new ServiceCollection().BuildServiceProvider());

    [Fact]
    public void Command_is_named_doctor()
    {
        BuildDoctor().Name.ShouldBe("doctor");
    }

    [Fact]
    public void Diagnose_is_no_longer_a_spelling_of_doctor()
    {
        var root = new RootCommand("test root");
        root.Subcommands.Add(BuildDoctor());

        var result = root.Parse("diagnose");

        result.Errors.ShouldNotBeEmpty();
    }
}
