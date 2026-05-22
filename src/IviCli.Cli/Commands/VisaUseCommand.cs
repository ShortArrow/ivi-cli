using System.CommandLine;
using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Devices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>
/// Wires the <c>visa use</c> subcommand to <see cref="SetCurrentDeviceCommandHandler"/>.
/// </summary>
public static class VisaUseCommand
{
    /// <summary>Builds the configured <see cref="Command"/>.</summary>
    public static Command Build(IServiceProvider services)
    {
        var nameArg = new Argument<string>("name")
        {
            Description = "The device alias to make current.",
        };
        var defaultOpt = new Option<bool>("--default")
        {
            Description = "Also persist the alias as the default device in config.toml.",
        };

        var command = new Command("use", "Set the current device target.");
        command.Arguments.Add(nameArg);
        command.Options.Add(defaultOpt);

        command.SetAction(
            async (parseResult, ct) =>
            {
                var name = parseResult.GetRequiredValue(nameArg);
                var persist = parseResult.GetValue(defaultOpt);

                var handler = services.GetRequiredService<SetCurrentDeviceCommandHandler>();
                var logger = services.GetRequiredService<ILogger<SetCurrentDeviceCommandHandler>>();

                var result = await handler.HandleAsync(
                    new SetCurrentDeviceCommand(name, persist),
                    ct
                );

                return result switch
                {
                    Result<DeviceName, SetCurrentDeviceError>.Ok ok => Success(ok.Value, persist),
                    Result<DeviceName, SetCurrentDeviceError>.Error err => Fail(err.Err, logger),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );

        return command;
    }

    private static int Success(DeviceName name, bool persisted)
    {
        Console.WriteLine(
            persisted ? $"using device {name.Value} (default)" : $"using device {name.Value}"
        );
        return ExitCodeMapper.Success;
    }

    private static int Fail(SetCurrentDeviceError error, ILogger logger)
    {
        logger.Log(
            Logging.SerilogConfiguration.ToLogLevel(error.Severity),
            error.Cause,
            error.Message,
            error.LogArgs.ToArray()
        );
        Console.Error.WriteLine(UserFacingMessage(error));
        return error switch
        {
            SetCurrentDeviceInvalidName => ExitCodeMapper.UsageError,
            SetCurrentDeviceUnknown => ExitCodeMapper.DeviceError,
            SetCurrentDeviceConfigFailure or SetCurrentDeviceSessionFailure =>
                ExitCodeMapper.ConfigurationError,
            _ => ExitCodeMapper.GenericFailure,
        };
    }

    private static string UserFacingMessage(SetCurrentDeviceError error) =>
        error switch
        {
            SetCurrentDeviceInvalidName n => $"error: invalid device name '{n.Raw}'.",
            SetCurrentDeviceUnknown u => $"error: no device named '{u.Name.Value}'.",
            SetCurrentDeviceConfigFailure => "error: configuration storage failed.",
            SetCurrentDeviceSessionFailure => "error: session storage failed.",
            _ => "error: failed to set current device.",
        };
}
