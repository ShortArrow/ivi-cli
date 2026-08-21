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
/// Wires the <c>ivicli driver</c> subcommand tree (PRD §6.5, ADR
/// 0045). v1 surfaces only <c>list</c> — operators inspecting which
/// IVI drivers their IVI Shared Components installation knows about
/// before they wire up a `[[devices]]` entry.
/// </summary>
public static class DriverCommand
{
    /// <summary>Builds the configured <see cref="Command"/>.</summary>
    public static Command Build(IServiceProvider services)
    {
        var command = new Command(
            "driver",
            "Inspect IVI drivers known to the local IVI Configuration Store."
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
        var cmd = new Command("list", "List installed IVI drivers (PRD §6.5).");
        cmd.Options.Add(jsonOpt);
        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var json = parseResult.GetValue(jsonOpt);
                var handler = services.GetRequiredService<ListDriversQueryHandler>();
                var logger = services.GetRequiredService<ILogger<ListDriversQueryHandler>>();

                var result = await handler.HandleAsync(new ListDriversQuery(), ct);
                return result switch
                {
                    Result<DriverListing, IviConfigurationStoreError>.Ok ok => Success(
                        ok.Value,
                        json
                    ),
                    Result<DriverListing, IviConfigurationStoreError>.Error err => Fail(
                        err.Err,
                        logger
                    ),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );
        return cmd;
    }

    private static int Success(DriverListing listing, bool emitJson)
    {
        if (emitJson)
        {
            Console.WriteLine(
                JsonSerializer.Serialize(
                    listing
                        .Drivers.Select(d => new DriverView(
                            d.Name,
                            d.Description,
                            d.ModulePath,
                            d.Prefix
                        ))
                        .ToArray(),
                    CliJsonContext.Default.DriverViewArray
                )
            );
        }
        else if (listing.Drivers.IsEmpty)
        {
            Console.WriteLine("(no IVI drivers found in the store)");
        }
        else
        {
            Console.WriteLine(
                string.Format(CultureInfo.InvariantCulture, "{0,-32} {1}", "NAME", "MODULE PATH")
            );
            foreach (var d in listing.Drivers)
            {
                Console.WriteLine(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0,-32} {1}",
                        d.Name,
                        d.ModulePath ?? "(unknown)"
                    )
                );
                if (!string.IsNullOrEmpty(d.Description))
                {
                    Console.WriteLine($"    {d.Description}");
                }
            }
        }
        return ExitCodeMapper.Success;
    }

    private static int Fail(IviConfigurationStoreError error, ILogger logger)
    {
        // Store-not-found is the common case on non-Windows hosts; surface
        // it at Information instead of stderr noise.
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
