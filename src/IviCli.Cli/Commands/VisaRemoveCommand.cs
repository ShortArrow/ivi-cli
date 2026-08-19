using System.CommandLine;
using IviCli.Application.Devices;
using IviCli.Application.Logging;
using IviCli.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>
/// Wires the <c>visa remove</c> subcommand to <see cref="RemoveDeviceCommandHandler"/>.
/// </summary>
public static class VisaRemoveCommand
{
    /// <summary>Builds the configured <see cref="Command"/>.</summary>
    public static Command Build(IServiceProvider services)
    {
        var nameArg = new Argument<string>("name") { Description = "The device alias to remove." };

        var command = new Command("remove", "Remove a registered device alias.");
        command.Arguments.Add(nameArg);

        command.SetAction(
            async (parseResult, ct) =>
            {
                var name = parseResult.GetRequiredValue(nameArg);
                var handler = services.GetRequiredService<RemoveDeviceCommandHandler>();
                var logger = services.GetRequiredService<ILogger<RemoveDeviceCommandHandler>>();

                var result = await handler.HandleAsync(new RemoveDeviceCommand(name), ct);

                return result switch
                {
                    Result<Domain.Devices.DeviceName, RemoveDeviceError>.Ok ok => Success(ok.Value),
                    Result<Domain.Devices.DeviceName, RemoveDeviceError>.Error err => Fail(
                        err.Err,
                        logger
                    ),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );

        return command;
    }

    private static int Success(Domain.Devices.DeviceName name)
    {
        Console.WriteLine($"removed device {name.Value}");
        return ExitCodeMapper.Success;
    }

    private static int Fail(RemoveDeviceError error, ILogger logger)
    {
        logger.LogIviError(error);
        Console.Error.WriteLine(UserFacingMessage(error));
        return error switch
        {
            RemoveDeviceInvalidName => ExitCodeMapper.UsageError,
            RemoveDeviceNotFound => ExitCodeMapper.DeviceError,
            RemoveDeviceStorageFailure => ExitCodeMapper.ConfigurationError,
            _ => ExitCodeMapper.GenericFailure,
        };
    }

    private static string UserFacingMessage(RemoveDeviceError error) =>
        error switch
        {
            RemoveDeviceInvalidName n => DeviceNameMessage.Invalid(n.Raw),
            RemoveDeviceNotFound nf => $"error: no device named '{nf.Name.Value}'.",
            RemoveDeviceStorageFailure => "error: configuration storage failed.",
            _ => "error: failed to remove device.",
        };
}
