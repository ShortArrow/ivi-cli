using System.CommandLine;
using IviCli.Application.Devices;
using IviCli.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>Wires the <c>visa read</c> subcommand.</summary>
public static class VisaReadCommand
{
    /// <summary>Builds the configured <see cref="Command"/>.</summary>
    public static Command Build(IServiceProvider services)
    {
        var nameArg = new Argument<string?>("name")
        {
            Description = "Optional device alias. Defaults to the current device.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var command = new Command(
            "read",
            "Read any pending response from the current (or named) device."
        );
        command.Arguments.Add(nameArg);

        command.SetAction(
            async (parseResult, ct) =>
            {
                var name = parseResult.GetValue(nameArg);
                var handler = services.GetRequiredService<ReadDeviceCommandHandler>();
                var logger = services.GetRequiredService<ILogger<ReadDeviceCommandHandler>>();

                var result = await handler.HandleAsync(new ReadDeviceCommand(name), ct);
                return result switch
                {
                    Result<string, ReadDeviceError>.Ok ok => Success(ok.Value),
                    Result<string, ReadDeviceError>.Error err => Fail(err.Err, logger),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );

        return command;
    }

    private static int Success(string response)
    {
        Console.WriteLine(response);
        return ExitCodeMapper.Success;
    }

    private static int Fail(ReadDeviceError error, ILogger logger)
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
            ReadDeviceInvalidName or ReadDeviceNoTarget => ExitCodeMapper.UsageError,
            ReadDeviceUnknown => ExitCodeMapper.DeviceError,
            ReadDeviceTransportFailure => ExitCodeMapper.TransportError,
            ReadDeviceConfigFailure or ReadDeviceSessionFailure =>
                ExitCodeMapper.ConfigurationError,
            _ => ExitCodeMapper.GenericFailure,
        };
    }

    private static string UserFacingMessage(ReadDeviceError error) =>
        error switch
        {
            ReadDeviceInvalidName n => $"error: invalid device name '{n.Raw}'.",
            ReadDeviceNoTarget => "error: no current device.",
            ReadDeviceUnknown u => $"error: no device named '{u.Name.Value}'.",
            ReadDeviceTransportFailure => "error: transport failure during read.",
            ReadDeviceConfigFailure => "error: configuration storage failed.",
            ReadDeviceSessionFailure => "error: session storage failed.",
            _ => "error: read failed.",
        };
}
