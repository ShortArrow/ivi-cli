using System.CommandLine;
using IviCli.Application.Logging;
using IviCli.Application.Session;
using IviCli.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>
/// Wires the <c>visa current</c> subcommand to <see cref="GetCurrentDeviceQueryHandler"/>.
/// </summary>
public static class VisaCurrentCommand
{
    /// <summary>Builds the configured <see cref="Command"/>.</summary>
    public static Command Build(IServiceProvider services)
    {
        var jsonOpt = new Option<bool>("--json") { Description = "Emit machine-readable JSON." };

        var command = new Command("current", "Show the currently selected device.");
        command.Options.Add(jsonOpt);

        command.SetAction(
            async (parseResult, ct) =>
            {
                var json = parseResult.GetValue(jsonOpt);
                var handler = services.GetRequiredService<GetCurrentDeviceQueryHandler>();
                var logger = services.GetRequiredService<ILogger<GetCurrentDeviceQueryHandler>>();

                var result = await handler.HandleAsync(new GetCurrentDeviceQuery(), ct);

                return result switch
                {
                    Result<CurrentDevice, GetCurrentDeviceError>.Ok ok => Success(ok.Value, json),
                    Result<CurrentDevice, GetCurrentDeviceError>.Error err => Fail(err.Err, logger),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );

        return command;
    }

    private static int Success(CurrentDevice current, bool emitJson)
    {
        if (emitJson)
        {
            Console.Write("{\"current\":");
            Console.Write(current.Name is { } name ? $"\"{name.Value}\"" : "null");
            Console.WriteLine("}");
        }
        else
        {
            Console.WriteLine(current.Name is { } name ? name.Value : "(no current device)");
        }
        return ExitCodeMapper.Success;
    }

    private static int Fail(GetCurrentDeviceError error, ILogger logger)
    {
        logger.LogIviError(error);
        Console.Error.WriteLine("error: failed to read current device.");
        return ExitCodeMapper.ConfigurationError;
    }
}
