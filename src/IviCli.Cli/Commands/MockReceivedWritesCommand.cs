using System.CommandLine;
using System.Text.Json;
using IviCli.Application.Capture;
using IviCli.Application.Mock;
using IviCli.Cli.Paths;
using IviCli.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>
/// Wires <c>ivicli mock received &lt;device&gt;</c>: reads the NDJSON traffic
/// capture a serving gateway produced (enabled with <c>IVICLI_CAPTURE</c>) and
/// reports the SCPI writes a device received — so a separate process can
/// confirm out-of-band that a client's write actually reached the mock.
/// </summary>
public static class MockReceivedWritesCommand
{
    /// <summary>Builds the configured <see cref="Command"/>.</summary>
    public static Command Build(IServiceProvider services)
    {
        var deviceArg = new Argument<string>("device")
        {
            Description = "Device alias whose received writes to report.",
        };
        var matchOpt = new Option<string?>("--match")
        {
            Description = "Only writes whose SCPI contains this substring (e.g. ':VOLT').",
        };
        var captureOpt = new Option<string?>("--capture")
        {
            Description =
                "NDJSON capture path. Defaults to IVICLI_CAPTURE; a relative path resolves under the log directory.",
        };
        var allOpt = new Option<bool>("--all")
        {
            Description = "List every matching write (oldest first) instead of only the last.",
        };
        var jsonOpt = new Option<bool>("--json") { Description = "Emit machine-readable JSON." };

        var command = new Command(
            "received",
            "Report the SCPI writes a device received, read back from an IVICLI_CAPTURE traffic log."
        );
        command.Arguments.Add(deviceArg);
        command.Options.Add(matchOpt);
        command.Options.Add(captureOpt);
        command.Options.Add(allOpt);
        command.Options.Add(jsonOpt);

        command.SetAction(
            async (parseResult, ct) =>
            {
                var device = parseResult.GetRequiredValue(deviceArg);
                var match = parseResult.GetValue(matchOpt);
                var all = parseResult.GetValue(allOpt);
                var json = parseResult.GetValue(jsonOpt);

                var capture =
                    parseResult.GetValue(captureOpt)
                    ?? Environment.GetEnvironmentVariable("IVICLI_CAPTURE");
                if (string.IsNullOrWhiteSpace(capture))
                {
                    Console.Error.WriteLine(
                        "error: no capture log. Pass --capture <path> or set IVICLI_CAPTURE when starting the gateway."
                    );
                    return ExitCodeMapper.UsageError;
                }
                var path = Path.IsPathRooted(capture)
                    ? capture
                    : Path.Combine(IviPaths.ResolveLogDirectory(), capture);

                var handler = services.GetRequiredService<MockReceivedWritesQueryHandler>();
                var logger = services.GetRequiredService<ILogger<MockReceivedWritesQueryHandler>>();

                var result = await handler.HandleAsync(
                    new MockReceivedWritesQuery(device, match, path),
                    ct
                );
                return result switch
                {
                    Result<
                        System.Collections.Immutable.ImmutableArray<TrafficEvent>,
                        MockReceivedWritesError
                    >.Ok ok => Render(ok.Value, all, json, Console.Out),
                    Result<
                        System.Collections.Immutable.ImmutableArray<TrafficEvent>,
                        MockReceivedWritesError
                    >.Error err => Fail(err.Err, logger),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );

        return command;
    }

    /// <summary>
    /// Renders the selected writes and returns the exit code. Non-empty →
    /// <see cref="ExitCodeMapper.Success"/>; empty → a distinct non-zero exit so
    /// a caller can assert "did NOT arrive" without parsing stdout.
    /// </summary>
    public static int Render(
        System.Collections.Immutable.ImmutableArray<TrafficEvent> writes,
        bool all,
        bool json,
        TextWriter output
    )
    {
        if (writes.IsEmpty)
        {
            if (json)
            {
                output.WriteLine(all ? "[]" : "null");
            }
            return ExitCodeMapper.GenericFailure;
        }

        var selected = all
            ? writes
            : System.Collections.Immutable.ImmutableArray.Create(writes[^1]);

        if (json)
        {
            var views = selected.Select(w => new WriteView(w.Device, w.Data ?? "", w.Timestamp));
            output.WriteLine(
                all
                    ? JsonSerializer.Serialize(views, JsonOptions)
                    : JsonSerializer.Serialize(views.First(), JsonOptions)
            );
        }
        else
        {
            foreach (var w in selected)
            {
                output.WriteLine(w.Data);
            }
        }

        return ExitCodeMapper.Success;
    }

    private static int Fail(MockReceivedWritesError error, ILogger logger)
    {
        logger.Log(
            Logging.SerilogConfiguration.ToLogLevel(error.Severity),
            error.Cause,
            error.Message,
            error.LogArgs.ToArray()
        );
        Console.Error.WriteLine("error: could not read received writes.");
        return error switch
        {
            MockReceivedWritesInvalidDevice => ExitCodeMapper.UsageError,
            MockReceivedWritesIoFailure => ExitCodeMapper.ConfigurationError,
            _ => ExitCodeMapper.GenericFailure,
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record WriteView(string Device, string Scpi, DateTimeOffset Timestamp);
}
