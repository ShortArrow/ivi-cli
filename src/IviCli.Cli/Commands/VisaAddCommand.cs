using System.CommandLine;
using IviCli.Application.Devices;
using IviCli.Application.Logging;
using IviCli.Domain;
using IviCli.Domain.Devices;
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
            Description =
                "The VISA resource string (for example TCPIP0::host::inst0::INSTR or ASRL/dev/ttyUSB0::INSTR).",
        };
        var timeoutOpt = new Option<int>("--timeout-ms")
        {
            Description = "Per-device default operation timeout, in milliseconds.",
            DefaultValueFactory = _ => 3000,
        };
        var baudOpt = new Option<int?>("--baud")
        {
            Description = "Serial baud rate (ASRL only; default 9600).",
        };
        var dataBitsOpt = new Option<int?>("--data-bits")
        {
            Description = "Serial data bits, 5 to 8 (ASRL only; default 8).",
        };
        var parityOpt = new Option<string?>("--parity")
        {
            Description =
                "Serial parity: none, odd, even, mark or space (ASRL only; default none).",
        };
        var stopBitsOpt = new Option<string?>("--stop-bits")
        {
            Description = "Serial stop bits: 1, 1.5 or 2 (ASRL only; default 1).",
        };
        var flowControlOpt = new Option<string?>("--flow-control")
        {
            Description =
                "Serial flow control: none, xon_xoff or rts_cts (ASRL only; default none).",
        };
        var readTerminationOpt = new Option<string?>("--read-termination")
        {
            Description =
                @"What ends a reply on SOCKET and serial: lf, cr or crlf (or \n, \r, \r\n).",
        };
        var writeTerminationOpt = new Option<string?>("--write-termination")
        {
            Description =
                @"What ends a command on SOCKET and serial: lf, cr or crlf (or \n, \r, \r\n).",
        };

        var command = new Command("add", "Register a new device alias.");
        command.Arguments.Add(nameArg);
        command.Arguments.Add(resourceArg);
        command.Options.Add(timeoutOpt);
        command.Options.Add(baudOpt);
        command.Options.Add(dataBitsOpt);
        command.Options.Add(parityOpt);
        command.Options.Add(stopBitsOpt);
        command.Options.Add(flowControlOpt);
        command.Options.Add(readTerminationOpt);
        command.Options.Add(writeTerminationOpt);

        command.SetAction(
            async (parseResult, ct) =>
            {
                var serial = new SerialOptions(
                    parseResult.GetValue(baudOpt),
                    parseResult.GetValue(dataBitsOpt),
                    parseResult.GetValue(parityOpt),
                    parseResult.GetValue(stopBitsOpt),
                    parseResult.GetValue(flowControlOpt)
                );
                var addCommand = new AddDeviceCommand(
                    parseResult.GetRequiredValue(nameArg),
                    parseResult.GetRequiredValue(resourceArg),
                    parseResult.GetValue(timeoutOpt)
                )
                {
                    Serial = serial == new SerialOptions() ? null : serial,
                    ReadTermination = parseResult.GetValue(readTerminationOpt),
                    WriteTermination = parseResult.GetValue(writeTerminationOpt),
                };

                var handler = services.GetRequiredService<AddDeviceCommandHandler>();
                var logger = services.GetRequiredService<ILogger<AddDeviceCommandHandler>>();
                var result = await handler.HandleAsync(addCommand, ct);

                return result switch
                {
                    Result<Device, AddDeviceError>.Ok ok => ReportSuccess(ok.Value),
                    Result<Device, AddDeviceError>.Error err => ReportFailure(err.Err, logger),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );

        return command;
    }

    private static int ReportSuccess(Device device)
    {
        Console.WriteLine($"added device {device.Name.Value}");
        if (device.IgnoresTermination)
        {
            Console.Error.WriteLine(TerminationWarning.For(device));
        }
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
            AddDeviceInvalidSerialSetting s => $"error: invalid {s.Field} '{s.Raw}'.",
            AddDeviceSerialOnNonSerial s =>
                $"error: serial settings apply only to an ASRL device, and '{s.Name.Value}' is not one.",
            AddDeviceInvalidTermination t =>
                $@"error: invalid termination '{t.Raw}'; use lf, cr or crlf (or \n, \r, \r\n).",
            AddDeviceStorageFailure s =>
                $"error: configuration storage failed ({s.Inner.Message.Replace("{Reason}", "...", StringComparison.Ordinal)}).",
            _ => "error: failed to add device.",
        };
}
