using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using IviCli.Application.Drivers;
using IviCli.Application.Logging;
using IviCli.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>
/// Wires the <c>ivicli logical</c> subcommand tree (PRD §6.5, ADR
/// 0045). v1 surfaces only <c>list</c> — operators inspecting which
/// IVI logical names are configured before they bind a device alias
/// to one.
/// </summary>
public static class LogicalCommand
{
    /// <summary>Builds the configured <see cref="Command"/>.</summary>
    public static Command Build(IServiceProvider services)
    {
        var command = new Command(
            "logical",
            "Inspect IVI logical names known to the local IVI Configuration Store."
        );
        command.Subcommands.Add(BuildList(services));
        return command;
    }

    private static Command BuildList(IServiceProvider services)
    {
        var jsonOpt = new Option<bool>("--json")
        {
            Description = "Emit machine-readable JSON instead of the human-readable table.",
        };
        var cmd = new Command("list", "List configured IVI logical names (PRD §6.5).");
        cmd.Options.Add(jsonOpt);
        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var json = parseResult.GetValue(jsonOpt);
                var handler = services.GetRequiredService<ListLogicalNamesQueryHandler>();
                var logger = services.GetRequiredService<ILogger<ListLogicalNamesQueryHandler>>();

                var result = await handler.HandleAsync(new ListLogicalNamesQuery(), ct);
                return result switch
                {
                    Result<LogicalNameListing, IviConfigurationStoreError>.Ok ok => Success(
                        ok.Value,
                        json
                    ),
                    Result<LogicalNameListing, IviConfigurationStoreError>.Error err => Fail(
                        err.Err,
                        logger
                    ),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );
        return cmd;
    }

    private static int Success(LogicalNameListing listing, bool emitJson)
    {
        if (emitJson)
        {
            Console.WriteLine(
                JsonSerializer.Serialize(
                    listing
                        .LogicalNames.Select(n => new LogicalNameView(
                            n.Name,
                            n.Description,
                            n.DriverSessionName
                        ))
                        .ToArray(),
                    CliJsonContext.Default.LogicalNameViewArray
                )
            );
        }
        else if (listing.LogicalNames.IsEmpty)
        {
            Console.WriteLine("(no IVI logical names found in the store)");
        }
        else
        {
            Console.WriteLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0,-24} {1,-24} {2}",
                    "NAME",
                    "SESSION",
                    "DESCRIPTION"
                )
            );
            foreach (var n in listing.LogicalNames)
            {
                Console.WriteLine(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0,-24} {1,-24} {2}",
                        n.Name,
                        n.DriverSessionName ?? "(unbound)",
                        n.Description ?? ""
                    )
                );
            }
        }
        return ExitCodeMapper.Success;
    }

    private static int Fail(IviConfigurationStoreError error, ILogger logger)
    {
        if (error is IviConfigurationStoreNotFound notFound)
        {
            Console.WriteLine($"(no IVI Configuration Store at {notFound.Path})");
            return ExitCodeMapper.Success;
        }
        logger.LogIviError(error);
        Console.Error.WriteLine(
            $"error: {error.Message.Replace("{Path}", "...", StringComparison.Ordinal)}"
        );
        return ExitCodeMapper.ConfigurationError;
    }
}
