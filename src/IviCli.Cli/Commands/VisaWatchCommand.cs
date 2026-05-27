using System.Collections.Immutable;
using System.CommandLine;
using IviCli.Application.Watch;
using IviCli.Cli.Watch;
using IviCli.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>
/// Wires the <c>visa watch</c> subcommand (PRD §15 / ADR 0030). Default
/// rendering is a Spectre.Console live table; <c>--json</c> emits NDJSON
/// to stdout; <c>--plain</c> emits ANSI-free table snapshots.
/// </summary>
public static class VisaWatchCommand
{
    /// <summary>Builds the configured <see cref="Command"/>.</summary>
    public static Command Build(IServiceProvider services)
    {
        var devicesArg = new Argument<string[]>("devices")
        {
            Description = "Device aliases to watch. Omit to watch every registered device.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var intervalOpt = new Option<int>("--interval", "-i")
        {
            Description = "Poll interval in milliseconds (default 1000).",
            DefaultValueFactory = _ => 1000,
        };
        var countOpt = new Option<int?>("--count", "-n")
        {
            Description = "Stop after this many ticks (default: run until Ctrl+C).",
        };
        var jsonOpt = new Option<bool>("--json")
        {
            Description = "Emit one NDJSON object per tick instead of a live table.",
        };
        var plainOpt = new Option<bool>("--plain")
        {
            Description =
                "Emit ANSI-free plain-text snapshots per tick (suitable for CI / log capture).",
        };

        var command = new Command(
            "watch",
            "Watch registered devices in a live table, refreshed at a fixed interval."
        );
        command.Arguments.Add(devicesArg);
        command.Options.Add(intervalOpt);
        command.Options.Add(countOpt);
        command.Options.Add(jsonOpt);
        command.Options.Add(plainOpt);

        command.SetAction(
            async (parseResult, ct) =>
            {
                var devices = parseResult.GetValue(devicesArg) ?? Array.Empty<string>();
                var interval = parseResult.GetValue(intervalOpt);
                var count = parseResult.GetValue(countOpt);
                var json = parseResult.GetValue(jsonOpt);
                var plain = parseResult.GetValue(plainOpt);

                if (json && plain)
                {
                    Console.Error.WriteLine("error: --json and --plain are mutually exclusive.");
                    return ExitCodeMapper.UsageError;
                }

                var handler = services.GetRequiredService<WatchDevicesCommandHandler>();
                var logger = services.GetRequiredService<ILogger<WatchDevicesCommandHandler>>();

                IWatchDevicesSink sink;
                IAsyncDisposable? disposableSink = null;
                if (json)
                {
                    sink = new NdjsonSink();
                }
                else if (plain)
                {
                    sink = new PlainTableSink();
                }
                else
                {
                    var live = new SpectreLiveTableSink();
                    sink = live;
                    disposableSink = live;
                }

                try
                {
                    var names =
                        devices.Length == 0
                            ? (ImmutableArray<string>?)null
                            : devices.ToImmutableArray();
                    var result = await handler.HandleAsync(
                        new WatchDevicesCommand(
                            Names: names,
                            Interval: TimeSpan.FromMilliseconds(interval),
                            MaxIterations: count
                        ),
                        sink,
                        ct
                    );
                    return result switch
                    {
                        Result<Unit, WatchDevicesError>.Ok => ExitCodeMapper.Success,
                        Result<Unit, WatchDevicesError>.Error err => Fail(err.Err, logger),
                        _ => ExitCodeMapper.GenericFailure,
                    };
                }
                finally
                {
                    if (disposableSink is not null)
                    {
                        await disposableSink.DisposeAsync();
                    }
                }
            }
        );

        return command;
    }

    private static int Fail(WatchDevicesError error, ILogger logger)
    {
        logger.Log(
            Logging.SerilogConfiguration.ToLogLevel(error.Severity),
            default,
            error.Cause,
            error.Message,
            error.LogArgs.ToArray()
        );
        Console.Error.WriteLine(UserFacingMessage(error));
        return error switch
        {
            WatchInvalidName or WatchInvalidInterval => ExitCodeMapper.UsageError,
            WatchUnknownDevice or WatchNoDevices => ExitCodeMapper.DeviceError,
            WatchConfigFailure => ExitCodeMapper.ConfigurationError,
            _ => ExitCodeMapper.GenericFailure,
        };
    }

    private static string UserFacingMessage(WatchDevicesError error) =>
        error switch
        {
            WatchInvalidName n => $"error: invalid device name '{n.Raw}'.",
            WatchInvalidInterval i => $"error: interval must be positive (got {i.Given}).",
            WatchUnknownDevice u => $"error: no device named '{u.Name.Value}'.",
            WatchNoDevices => "error: no devices registered to watch.",
            WatchConfigFailure => "error: configuration storage failed.",
            _ => "error: watch failed.",
        };
}
