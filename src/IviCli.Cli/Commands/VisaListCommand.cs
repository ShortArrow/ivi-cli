using System.CommandLine;
using IviCli.Application.Devices;
using IviCli.Application.Logging;
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
        Console.Write(
            emitJson
                ? DeviceListingFormatter.FormatJson(listing)
                : DeviceListingFormatter.FormatHuman(listing)
        );
        return ExitCodeMapper.Success;
    }

    private static int ReportFailure(ListDevicesError error, ILogger logger)
    {
        logger.LogIviError(error);
        Console.Error.WriteLine("error: failed to list devices.");
        return ExitCodeMapper.Map(error);
    }
}
