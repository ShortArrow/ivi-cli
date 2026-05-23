using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using IviCli.Application.Scripting;
using IviCli.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>Wires the <c>visa monitor</c> subcommand (Phase 3 / ADR 0027).</summary>
public static class VisaMonitorCommand
{
    /// <summary>Builds the configured <see cref="Command"/>.</summary>
    public static Command Build(IServiceProvider services)
    {
        var queryArg = new Argument<string>("query")
        {
            Description = "SCPI query to repeat (must end with `?`).",
        };
        var deviceOpt = new Option<string?>("--device")
        {
            Description = "Target device alias (defaults to the session-current device).",
        };
        var intervalOpt = new Option<int>("--interval")
        {
            Description = "Poll interval in milliseconds (default 1000).",
            DefaultValueFactory = _ => 1000,
        };
        var countOpt = new Option<int?>("--count")
        {
            Description = "Stop after this many samples (default: run until Ctrl+C).",
        };
        var jsonOpt = new Option<bool>("--json")
        {
            Description = "Emit one JSON object per sample instead of plain text.",
        };

        var command = new Command(
            "monitor",
            "Poll a SCPI query at a fixed interval and stream timestamped responses."
        );
        command.Arguments.Add(queryArg);
        command.Options.Add(deviceOpt);
        command.Options.Add(intervalOpt);
        command.Options.Add(countOpt);
        command.Options.Add(jsonOpt);

        command.SetAction(
            async (parseResult, ct) =>
            {
                var query = parseResult.GetRequiredValue(queryArg);
                var device = parseResult.GetValue(deviceOpt);
                var interval = parseResult.GetValue(intervalOpt);
                var count = parseResult.GetValue(countOpt);
                var json = parseResult.GetValue(jsonOpt);

                var handler = services.GetRequiredService<MonitorDeviceCommandHandler>();
                var logger = services.GetRequiredService<ILogger<MonitorDeviceCommandHandler>>();

                Func<MonitorSample, Task> sink = json ? EmitJsonAsync : EmitTextAsync;

                var result = await handler.HandleAsync(
                    new MonitorDeviceCommand(
                        Name: device,
                        Query: query,
                        Interval: TimeSpan.FromMilliseconds(interval),
                        Count: count
                    ),
                    sink,
                    ct
                );
                return result switch
                {
                    Result<int, MonitorDeviceError>.Ok => ExitCodeMapper.Success,
                    Result<int, MonitorDeviceError>.Error err => Fail(err.Err, logger),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );

        return command;
    }

    private static Task EmitTextAsync(MonitorSample s)
    {
        Console.WriteLine(
            string.Create(CultureInfo.InvariantCulture, $"{s.Timestamp:O}  {s.Query}  {s.Response}")
        );
        return Task.CompletedTask;
    }

    private static Task EmitJsonAsync(MonitorSample s)
    {
        var payload = new
        {
            ts = s.Timestamp,
            seq = s.Sequence,
            query = s.Query,
            response = s.Response,
        };
        Console.WriteLine(JsonSerializer.Serialize(payload));
        return Task.CompletedTask;
    }

    private static int Fail(MonitorDeviceError error, ILogger logger)
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
            MonitorDeviceInvalidQuery
            or MonitorDeviceInvalidInterval
            or MonitorDeviceInvalidName
            or MonitorDeviceNoTarget => ExitCodeMapper.UsageError,
            MonitorDeviceUnknown => ExitCodeMapper.DeviceError,
            MonitorDeviceTransportFailure => ExitCodeMapper.TransportError,
            MonitorDeviceStoreFailure => ExitCodeMapper.ConfigurationError,
            _ => ExitCodeMapper.GenericFailure,
        };
    }

    private static string UserFacingMessage(MonitorDeviceError error) =>
        error switch
        {
            MonitorDeviceInvalidQuery q => $"error: invalid SCPI query '{q.Raw}'.",
            MonitorDeviceInvalidInterval => "error: --interval must be positive.",
            MonitorDeviceInvalidName n => $"error: invalid device name '{n.Raw}'.",
            MonitorDeviceNoTarget =>
                "error: no current device. Use `visa use <name>` first or pass --device.",
            MonitorDeviceUnknown u => $"error: no device named '{u.Name.Value}'.",
            MonitorDeviceTransportFailure => "error: transport failure during monitor.",
            MonitorDeviceStoreFailure => "error: configuration storage failed.",
            _ => "error: monitor failed.",
        };
}
