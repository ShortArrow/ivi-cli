using System.CommandLine;
using IviCli.Application.Devices;
using IviCli.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>
/// Wires the <c>visa list</c> subcommand to <see cref="ListDevicesQueryHandler"/>.
/// </summary>
public static class VisaListCommand
{
    /// <summary>Builds the configured <see cref="Command"/>.</summary>
    public static Command Build(IServiceProvider services)
    {
        var jsonOpt = new Option<bool>("--json")
        {
            Description = "Emit machine-readable JSON instead of the human-readable table.",
        };

        var command = new Command("list", "List configured devices.");
        command.Options.Add(jsonOpt);

        command.SetAction(
            async (parseResult, ct) =>
            {
                var json = parseResult.GetValue(jsonOpt);

                var handler = services.GetRequiredService<ListDevicesQueryHandler>();
                var logger = services.GetRequiredService<ILogger<ListDevicesQueryHandler>>();

                var result = await handler.HandleAsync(new ListDevicesQuery(), ct);

                return result switch
                {
                    Result<DeviceListing, ListDevicesError>.Ok ok => ReportSuccess(
                        ok.Value,
                        emitJson: json
                    ),
                    Result<DeviceListing, ListDevicesError>.Error err => ReportFailure(
                        err.Err,
                        logger
                    ),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );

        return command;
    }

    private static int ReportSuccess(DeviceListing listing, bool emitJson)
    {
        if (emitJson)
        {
            // Phase 1: minimal hand-rolled JSON to avoid pulling
            // System.Text.Json conventions in front of dedicated DTOs.
            Console.Write("{\"devices\":[");
            for (var i = 0; i < listing.Devices.Length; i++)
            {
                if (i > 0)
                {
                    Console.Write(",");
                }
                var d = listing.Devices[i];
                Console.Write(
                    $"{{\"name\":\"{d.Name.Value}\",\"timeout_ms\":{d.Timeout.Milliseconds}}}"
                );
            }
            Console.Write("],\"default\":");
            Console.Write(listing.DefaultDevice is { } def ? $"\"{def.Value}\"" : "null");
            Console.WriteLine("}");
        }
        else
        {
            if (listing.Devices.Length == 0)
            {
                Console.WriteLine("(no devices configured)");
            }
            else
            {
                foreach (var d in listing.Devices)
                {
                    var marker = listing.DefaultDevice == d.Name ? "*" : " ";
                    Console.WriteLine($"{marker} {d.Name.Value}\t{d.Timeout}");
                }
            }
        }
        return ExitCodeMapper.Success;
    }

    private static int ReportFailure(ListDevicesError error, ILogger logger)
    {
        logger.Log(
            Logging.SerilogConfiguration.ToLogLevel(error.Severity),
            error.Cause,
            error.Message,
            error.LogArgs.ToArray()
        );
        Console.Error.WriteLine("error: failed to list devices.");
        return ExitCodeMapper.Map(error);
    }
}
