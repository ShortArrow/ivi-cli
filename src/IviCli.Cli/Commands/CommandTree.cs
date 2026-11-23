using System.CommandLine;

namespace IviCli.Cli.Commands;

/// <summary>
/// The <c>ivicli</c> command tree: every command, argument and option the
/// CLI accepts. Building it resolves no services, so a test can build it
/// with an empty provider to read the CLI's surface.
/// </summary>
public static class CommandTree
{
    /// <summary>
    /// Builds the root command with every subcommand attached. Dynamic
    /// completion is not bound here; <c>Program</c> binds it after this
    /// returns.
    /// </summary>
    public static RootCommand Build(IServiceProvider services)
    {
        var visa = new Command("visa", "VISA transport / SCPI operations.");
        visa.Subcommands.Add(VisaAddCommand.Build(services));
        visa.Subcommands.Add(VisaRemoveCommand.Build(services));
        visa.Subcommands.Add(VisaListCommand.Build(services));
        visa.Subcommands.Add(VisaUseCommand.Build(services));
        visa.Subcommands.Add(VisaCurrentCommand.Build(services));
        visa.Subcommands.Add(VisaScanCommand.Build(services));
        visa.Subcommands.Add(VisaAsrlCommand.Build(services));
        visa.Subcommands.Add(VisaQueryCommand.Build(services));
        visa.Subcommands.Add(VisaWriteCommand.Build(services));
        visa.Subcommands.Add(VisaReadCommand.Build(services));
        visa.Subcommands.Add(VisaStatusCommand.Build(services));
        visa.Subcommands.Add(VisaScriptCommand.Build(services));
        visa.Subcommands.Add(VisaMonitorCommand.Build(services));
        visa.Subcommands.Add(VisaWatchCommand.Build(services));
        visa.Subcommands.Add(VisaLintCommand.Build(services));

        var root = new RootCommand(
            "ivi-cli: integrated CLI for managing, diagnosing, and operating VISA/IVI instruments."
        );
        GlobalLoggingOptions.AddTo(root);
        root.Subcommands.Add(visa);
        root.Subcommands.Add(MockCommand.Build(services));
        root.Subcommands.Add(ServerCommand.Build(services));
        root.Subcommands.Add(ApiCommand.Build(services));
        root.Subcommands.Add(DoctorCommand.Build(services));
        root.Subcommands.Add(DriverCommand.Build(services));
        root.Subcommands.Add(LogicalCommand.Build(services));
        root.Subcommands.Add(CompletionCommand.Build());
        root.Subcommands.Add(CompleteCommand.Build(root, services));
        return root;
    }
}
