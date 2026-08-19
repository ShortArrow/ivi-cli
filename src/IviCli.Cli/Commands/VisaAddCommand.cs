using System.CommandLine;
using IviCli.Application.Devices;
using IviCli.Application.Logging;
using IviCli.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>
/// Wires the <c>visa add</c> subcommand to <see cref="AddDeviceCommandHandler"/>.
/// </summary>
public static class VisaAddCommand
{
    /// <summary>Builds the configured <see cref="Command"/>.</summary>
    public static Command Build(IServiceProvider services)
    {
        var nameArg = new Argument<string>("name")
        {
            Description = "The device alias to register (for example psu1).",
        };
        var resourceArg = new Argument<string>("resource")
        {
            Description = "The VISA resource string (for example TCPIP0::host::inst0::INSTR).",
        };
        var timeoutOpt = new Option<int>("--timeout-ms")
        {
            Description = "Per-device default operation timeout, in milliseconds.",
            DefaultValueFactory = _ => 3000,
        };

        var command = new Command("add", "Register a new device alias.");
        command.Arguments.Add(nameArg);
        command.Arguments.Add(resourceArg);
        command.Options.Add(timeoutOpt);

        command.SetAction(
            async (parseResult, ct) =>
            {
                var name = parseResult.GetRequiredValue(nameArg);
                var resource = parseResult.GetRequiredValue(resourceArg);
                var timeoutMs = parseResult.GetValue(timeoutOpt);

                var handler = services.GetRequiredService<AddDeviceCommandHandler>();
                var logger = services.GetRequiredService<ILogger<AddDeviceCommandHandler>>();

                var result = await handler.HandleAsync(
                    new AddDeviceCommand(name, resource, timeoutMs),
                    ct
                );

                return result switch
                {
                    Result<Domain.Devices.DeviceName, AddDeviceError>.Ok ok => ReportSuccess(
                        ok.Value
                    ),
                    Result<Domain.Devices.DeviceName, AddDeviceError>.Error err => ReportFailure(
                        err.Err,
                        logger
                    ),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );

        return command;
    }

    private static int ReportSuccess(Domain.Devices.DeviceName name)
    {
        Console.WriteLine($"added device {name.Value}");
        return ExitCodeMapper.Success;
    }

    private static int ReportFailure(AddDeviceError error, ILogger logger)
    {
        logger.LogIviError(error);
        Console.Error.WriteLine(UserFacingMessage(error));
        return ExitCodeMapper.Map(error);
    }

    private static string UserFacingMessage(AddDeviceError error) =>
        error switch
        {
            AddDeviceInvalidName n => DeviceNameMessage.Invalid(n.Raw),
            AddDeviceInvalidResource r => $"error: invalid VISA resource '{r.Raw}'.",
            AddDeviceInvalidTimeout t => $"error: invalid timeout {t.RawMilliseconds}ms.",
            AddDeviceNameTaken nt => $"error: device '{nt.Name.Value}' already exists.",
            AddDeviceStorageFailure s =>
                $"error: configuration storage failed ({s.Inner.Message.Replace("{Reason}", "...", StringComparison.Ordinal)}).",
            _ => "error: failed to add device.",
        };
}
