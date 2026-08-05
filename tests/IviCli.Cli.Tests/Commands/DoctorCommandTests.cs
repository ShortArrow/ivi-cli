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

    /// <summary>
    /// Pins the deprecation schedule of the pre-rename spelling: below 0.3.0
    /// <c>diagnose</c> must keep resolving to <c>doctor</c>; from 0.3.0 on it
    /// must be gone. Bumping the version past the boundary turns this red
    /// until the alias is actually removed.
    /// </summary>
    [Fact]
    public void Diagnose_alias_is_kept_below_0_3_0_and_removed_from_0_3_0()
    {
        var root = new RootCommand("test root");
        var doctor = BuildDoctor();
        root.Subcommands.Add(doctor);

        var result = root.Parse("diagnose");

        var version = typeof(DoctorCommand).Assembly.GetName().Version!;
        if (version < new Version(0, 3, 0))
        {
            result.Errors.ShouldBeEmpty();
            result.CommandResult.Command.ShouldBeSameAs(doctor);
        }
        else
        {
            result.Errors.ShouldNotBeEmpty();
        }
    }
}
