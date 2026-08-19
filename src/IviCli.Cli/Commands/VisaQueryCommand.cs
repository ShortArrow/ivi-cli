using System.CommandLine;
using IviCli.Application.Devices;
using IviCli.Application.Logging;
using IviCli.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>Wires the <c>visa query</c> subcommand.</summary>
public static class VisaQueryCommand
{
    /// <summary>Builds the configured <see cref="Command"/>.</summary>
    public static Command Build(IServiceProvider services)
    {
        // Form A: visa query "*IDN?"           (uses current device)
        // Form B: visa query psu1 "*IDN?"      (explicit device)
        var firstArg = new Argument<string>("name-or-scpi")
        {
            Description = "Either the device alias or the SCPI text. See examples in --help.",
        };
        var secondArg = new Argument<string?>("scpi")
        {
            Description =
                "The SCPI query when an explicit alias is supplied as the first argument.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var command = new Command("query", "Send a SCPI query and print the response.");
        command.Arguments.Add(firstArg);
        command.Arguments.Add(secondArg);

        command.SetAction(
            async (parseResult, ct) =>
            {
                var first = parseResult.GetRequiredValue(firstArg);
                var second = parseResult.GetValue(secondArg);
                var (name, scpi) = SplitArgs(first, second);

                var handler = services.GetRequiredService<QueryDeviceCommandHandler>();
                var logger = services.GetRequiredService<ILogger<QueryDeviceCommandHandler>>();

                var result = await handler.HandleAsync(new QueryDeviceCommand(name, scpi), ct);
                return result switch
                {
                    Result<string, QueryDeviceError>.Ok ok => Success(ok.Value),
                    Result<string, QueryDeviceError>.Error err => Fail(err.Err, logger),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );

        return command;
    }

    private static (string? name, string scpi) SplitArgs(string first, string? second) =>
        second is null ? (null, first) : (first, second);

    private static int Success(string response)
    {
        Console.WriteLine(response);
        return ExitCodeMapper.Success;
    }

    private static int Fail(QueryDeviceError error, ILogger logger)
    {
        logger.LogIviError(error);
        Console.Error.WriteLine(UserFacingMessage(error));
        return error switch
        {
            QueryDeviceInvalidName or QueryDeviceInvalidScpi or QueryDeviceNoTarget =>
                ExitCodeMapper.UsageError,
            QueryDeviceUnknown => ExitCodeMapper.DeviceError,
            QueryDeviceTransportFailure => ExitCodeMapper.TransportError,
            QueryDeviceConfigFailure or QueryDeviceSessionFailure =>
                ExitCodeMapper.ConfigurationError,
            _ => ExitCodeMapper.GenericFailure,
        };
    }

    private static string UserFacingMessage(QueryDeviceError error) =>
        error switch
        {
            QueryDeviceInvalidName n => DeviceNameMessage.Invalid(n.Raw),
            QueryDeviceInvalidScpi s => $"error: invalid SCPI query ({s.Reason}).",
            QueryDeviceNoTarget =>
                "error: no current device. Use `visa use <name>` first or pass an alias.",
            QueryDeviceUnknown u => $"error: no device named '{u.Name.Value}'.",
            QueryDeviceTransportFailure => "error: transport failure during query.",
            QueryDeviceConfigFailure => "error: configuration storage failed.",
            QueryDeviceSessionFailure => "error: session storage failed.",
            _ => "error: query failed.",
        };
}
