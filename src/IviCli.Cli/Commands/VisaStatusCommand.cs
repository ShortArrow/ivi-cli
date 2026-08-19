using System.CommandLine;
using System.Globalization;
using IviCli.Application.Devices;
using IviCli.Application.Logging;
using IviCli.Domain;
using IviCli.Domain.Visa;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>Wires the <c>visa status</c> subcommand.</summary>
public static class VisaStatusCommand
{
    /// <summary>Builds the configured <see cref="Command"/>.</summary>
    public static Command Build(IServiceProvider services)
    {
        var nameArg = new Argument<string?>("name")
        {
            Description = "Optional device alias. Defaults to the current device.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var jsonOpt = new Option<bool>("--json") { Description = "Emit machine-readable JSON." };

        var command = new Command(
            "status",
            "Show connection state, response time, and IDN for the device."
        );
        command.Arguments.Add(nameArg);
        command.Options.Add(jsonOpt);

        command.SetAction(
            async (parseResult, ct) =>
            {
                var name = parseResult.GetValue(nameArg);
                var json = parseResult.GetValue(jsonOpt);

                var handler = services.GetRequiredService<StatusDeviceCommandHandler>();
                var logger = services.GetRequiredService<ILogger<StatusDeviceCommandHandler>>();

                var result = await handler.HandleAsync(new StatusDeviceCommand(name), ct);
                return result switch
                {
                    Result<DeviceStatus, StatusDeviceError>.Ok ok => Success(ok.Value, json),
                    Result<DeviceStatus, StatusDeviceError>.Error err => Fail(err.Err, logger),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );

        return command;
    }

    private static int Success(DeviceStatus status, bool emitJson)
    {
        var inv = CultureInfo.InvariantCulture;
        if (emitJson)
        {
            // Use the masked log form so JSON output is also safe to copy
            // into bug reports without leaking host/serial.
            var resource = status.Device.Resource.ToLogString();
            var idnJson = status.IdnResponse is null ? "null" : $"\"{Escape(status.IdnResponse)}\"";
            var failureJson = status.FailureMessage is null
                ? "null"
                : $"\"{Escape(status.FailureMessage)}\"";
            Console.WriteLine(
                string.Create(
                    inv,
                    $"{{\"device\":\"{status.Device.Name.Value}\",\"resource\":\"{resource}\",\"online\":{(status.IsOnline ? "true" : "false")},\"response_time_ms\":{status.ResponseTime.TotalMilliseconds:F1},\"idn\":{idnJson},\"failure\":{failureJson}}}"
                )
            );
        }
        else
        {
            Console.WriteLine(string.Create(inv, $"device:        {status.Device.Name.Value}"));
            Console.WriteLine(
                string.Create(inv, $"resource:      {status.Device.Resource.ToLogString()}")
            );
            Console.WriteLine(
                string.Create(inv, $"online:        {(status.IsOnline ? "yes" : "no")}")
            );
            Console.WriteLine(
                string.Create(inv, $"response time: {status.ResponseTime.TotalMilliseconds:F1} ms")
            );
            if (status.IdnResponse is not null)
            {
                Console.WriteLine(string.Create(inv, $"idn:           {status.IdnResponse}"));
            }
            if (status.FailureMessage is not null)
            {
                Console.WriteLine(string.Create(inv, $"failure:       {status.FailureMessage}"));
            }
        }
        return ExitCodeMapper.Success;
    }

    private static int Fail(StatusDeviceError error, ILogger logger)
    {
        logger.LogIviError(error);
        Console.Error.WriteLine(UserFacingMessage(error));
        return error switch
        {
            StatusDeviceInvalidName or StatusDeviceNoTarget => ExitCodeMapper.UsageError,
            StatusDeviceUnknown => ExitCodeMapper.DeviceError,
            StatusDeviceConfigFailure or StatusDeviceSessionFailure =>
                ExitCodeMapper.ConfigurationError,
            _ => ExitCodeMapper.GenericFailure,
        };
    }

    private static string UserFacingMessage(StatusDeviceError error) =>
        error switch
        {
            StatusDeviceInvalidName n => DeviceNameMessage.Invalid(n.Raw),
            StatusDeviceNoTarget => "error: no current device.",
            StatusDeviceUnknown u => $"error: no device named '{u.Name.Value}'.",
            StatusDeviceConfigFailure => "error: configuration storage failed.",
            StatusDeviceSessionFailure => "error: session storage failed.",
            _ => "error: status probe failed.",
        };

    private static string Escape(string raw) =>
        raw.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
