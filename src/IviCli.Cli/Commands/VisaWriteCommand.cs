using System.CommandLine;
using IviCli.Application.Devices;
using IviCli.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>Wires the <c>visa write</c> subcommand.</summary>
public static class VisaWriteCommand
{
    /// <summary>Builds the configured <see cref="Command"/>.</summary>
    public static Command Build(IServiceProvider services)
    {
        var firstArg = new Argument<string>("name-or-scpi")
        {
            Description = "Either the device alias or the SCPI text. See examples in --help.",
        };
        var secondArg = new Argument<string?>("scpi")
        {
            Description =
                "The SCPI command when an explicit alias is supplied as the first argument.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var command = new Command("write", "Send a SCPI command (no response expected).");
        command.Arguments.Add(firstArg);
        command.Arguments.Add(secondArg);

        command.SetAction(
            async (parseResult, ct) =>
            {
                var first = parseResult.GetRequiredValue(firstArg);
                var second = parseResult.GetValue(secondArg);
                var (name, scpi) = second is null ? (null, first) : (first, second);

                var handler = services.GetRequiredService<WriteDeviceCommandHandler>();
                var logger = services.GetRequiredService<ILogger<WriteDeviceCommandHandler>>();

                var result = await handler.HandleAsync(new WriteDeviceCommand(name, scpi), ct);
                return result switch
                {
                    Result<Unit, WriteDeviceError>.Ok => ExitCodeMapper.Success,
                    Result<Unit, WriteDeviceError>.Error err => Fail(err.Err, logger),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );

        return command;
    }

    private static int Fail(WriteDeviceError error, ILogger logger)
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
            WriteDeviceInvalidName or WriteDeviceInvalidScpi or WriteDeviceNoTarget =>
                ExitCodeMapper.UsageError,
            WriteDeviceUnknown => ExitCodeMapper.DeviceError,
            WriteDeviceTransportFailure => ExitCodeMapper.TransportError,
            WriteDeviceConfigFailure or WriteDeviceSessionFailure =>
                ExitCodeMapper.ConfigurationError,
            _ => ExitCodeMapper.GenericFailure,
        };
    }

    private static string UserFacingMessage(WriteDeviceError error) =>
        error switch
        {
            WriteDeviceInvalidName n => DeviceNameMessage.Invalid(n.Raw),
            WriteDeviceInvalidScpi s => $"error: invalid SCPI command ({s.Reason}).",
            WriteDeviceNoTarget => "error: no current device.",
            WriteDeviceUnknown u => $"error: no device named '{u.Name.Value}'.",
            WriteDeviceTransportFailure => "error: transport failure during write.",
            WriteDeviceConfigFailure => "error: configuration storage failed.",
            WriteDeviceSessionFailure => "error: session storage failed.",
            _ => "error: write failed.",
        };
}
