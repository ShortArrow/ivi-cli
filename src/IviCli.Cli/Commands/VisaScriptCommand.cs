using System.CommandLine;
using IviCli.Application.Scripting;
using IviCli.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>Wires the <c>visa script</c> subcommand (Phase 3 / ADR 0027).</summary>
public static class VisaScriptCommand
{
    /// <summary>Builds the configured <see cref="Command"/>.</summary>
    public static Command Build(IServiceProvider services)
    {
        var fileArg = new Argument<string>("file")
        {
            Description = "Path to a SCPI script file (see ADR 0027 §2 for directives).",
        };
        var deviceOpt = new Option<string?>("--device")
        {
            Description = "Target device alias (defaults to the session-current device).",
        };

        var command = new Command(
            "script",
            "Execute a SCPI script file against the active device."
        );
        command.Arguments.Add(fileArg);
        command.Options.Add(deviceOpt);

        command.SetAction(
            async (parseResult, ct) =>
            {
                var file = parseResult.GetRequiredValue(fileArg);
                var device = parseResult.GetValue(deviceOpt);

                if (!File.Exists(file))
                {
                    Console.Error.WriteLine($"error: script file not found: {file}");
                    return ExitCodeMapper.UsageError;
                }

                var source = await File.ReadAllTextAsync(file, ct);
                var handler = services.GetRequiredService<ScriptDeviceCommandHandler>();
                var logger = services.GetRequiredService<ILogger<ScriptDeviceCommandHandler>>();

                var result = await handler.HandleAsync(
                    new ScriptDeviceCommand(device, source),
                    ct
                );
                return result switch
                {
                    Result<ScriptExecutionReport, ScriptDeviceError>.Ok ok => Render(ok.Value),
                    Result<ScriptExecutionReport, ScriptDeviceError>.Error err => Fail(
                        err.Err,
                        logger
                    ),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );

        return command;
    }

    private static int Render(ScriptExecutionReport report)
    {
        foreach (var line in report.Output)
        {
            Console.WriteLine(line);
        }
        return ExitCodeMapper.Success;
    }

    private static int Fail(ScriptDeviceError error, ILogger logger)
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
            ScriptDeviceParseFailure or ScriptDeviceInvalidName or ScriptDeviceNoTarget =>
                ExitCodeMapper.UsageError,
            ScriptDeviceUnknown => ExitCodeMapper.DeviceError,
            ScriptDeviceTransportFailure => ExitCodeMapper.TransportError,
            ScriptDeviceAssertFailure => ExitCodeMapper.GenericFailure,
            ScriptDeviceStoreFailure => ExitCodeMapper.ConfigurationError,
            _ => ExitCodeMapper.GenericFailure,
        };
    }

    private static string UserFacingMessage(ScriptDeviceError error) =>
        error switch
        {
            ScriptDeviceParseFailure p => $"error: script parse failed ({p.Inner.Message}).",
            ScriptDeviceInvalidName n => $"error: invalid device name '{n.Raw}'.",
            ScriptDeviceNoTarget =>
                "error: no current device. Use `visa use <name>` first or pass --device.",
            ScriptDeviceUnknown u => $"error: no device named '{u.Name.Value}'.",
            ScriptDeviceTransportFailure t => $"error: transport failure on script line {t.Line}.",
            ScriptDeviceAssertFailure a =>
                $"error: assert /{a.Pattern}/ failed on line {a.Line} (actual: {a.Actual}).",
            ScriptDeviceStoreFailure => "error: configuration storage failed.",
            _ => "error: script execution failed.",
        };
}
