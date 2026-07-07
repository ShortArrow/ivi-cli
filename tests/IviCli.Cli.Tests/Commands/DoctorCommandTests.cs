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
    public void Diagnose_remains_a_deprecated_alias()
    {
        BuildDoctor().Aliases.ShouldContain("diagnose");
    }

    [Fact]
    public void Parsing_the_alias_resolves_to_the_doctor_command()
    {
        var root = new RootCommand("test root");
        var doctor = BuildDoctor();
        root.Subcommands.Add(doctor);

        var result = root.Parse("diagnose");

        result.Errors.ShouldBeEmpty();
        result.CommandResult.Command.ShouldBeSameAs(doctor);
    }
}
