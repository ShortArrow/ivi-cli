using System.CommandLine;
using System.Globalization;
using System.Text;
using System.Text.Json;
using IviCli.Application.Devices;
using IviCli.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>
/// Wires the <c>visa asrl</c> subcommand tree, which manages the machine's
/// table of serial board numbers (ADR 0055 §3).
/// </summary>
public static class VisaAsrlCommand
{
    /// <summary>Returns the <c>asrl</c> command, ready to attach under <c>visa</c>.</summary>
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("asrl", "Map serial board numbers (ASRL<n>::INSTR) to ports.");
        command.Subcommands.Add(BuildList(services));
        command.Subcommands.Add(BuildAdd(services));
        command.Subcommands.Add(BuildRemove(services));
        return command;
    }

    private static Command BuildList(IServiceProvider services)
    {
        var jsonOpt = new Option<bool>("--json") { Description = "Emit the listing as JSON." };
        var cmd = new Command("list", "List the registered board numbers.");
        cmd.Options.Add(jsonOpt);
        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var handler = services.GetRequiredService<ListAsrlBoardsQueryHandler>();
                var logger = services.GetRequiredService<ILogger<ListAsrlBoardsQueryHandler>>();
                var result = await handler.HandleAsync(new ListAsrlBoardsQuery(), ct);
                switch (result)
                {
                    case Result<AsrlBoardListing, ListAsrlBoardsError>.Ok ok
                        when parseResult.GetValue(jsonOpt):
                        Console.Out.Write(RenderJson(ok.Value));
                        return ExitCodeMapper.Success;
                    case Result<AsrlBoardListing, ListAsrlBoardsError>.Ok ok:
                        return Render(ok.Value, Console.Out);
                    case Result<AsrlBoardListing, ListAsrlBoardsError>.Error err:
                        return ServerCommand.Log(
                            err.Err,
                            logger,
                            "error: failed to list ASRL boards.",
                            ExitCodeMapper.ConfigurationError
                        );
                    default:
                        return ExitCodeMapper.GenericFailure;
                }
            }
        );
        return cmd;
    }

    private static Command BuildAdd(IServiceProvider services)
    {
        var boardArg = new Argument<int>("board")
        {
            Description = "The board number, as in ASRL<board>::INSTR.",
        };
        var portArg = new Argument<string>("port")
        {
            Description = "The port, for example /dev/ttyUSB0 or COM3.",
        };
        var cmd = new Command("add", "Register the port a board number means on this machine.");
        cmd.Arguments.Add(boardArg);
        cmd.Arguments.Add(portArg);
        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var handler = services.GetRequiredService<AddAsrlBoardCommandHandler>();
                var logger = services.GetRequiredService<ILogger<AddAsrlBoardCommandHandler>>();
                var result = await handler.HandleAsync(
                    new AddAsrlBoardCommand(
                        parseResult.GetValue(boardArg),
                        parseResult.GetRequiredValue(portArg)
                    ),
                    ct
                );
                return result switch
                {
                    Result<AsrlBoardAdded, AddAsrlBoardError>.Ok ok => ReportAdded(ok.Value),
                    Result<AsrlBoardAdded, AddAsrlBoardError>.Error err => ServerCommand.Log(
                        err.Err,
                        logger,
                        AddFailureMessage(err.Err),
                        AddExitCode(err.Err)
                    ),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );
        return cmd;
    }

    private static Command BuildRemove(IServiceProvider services)
    {
        var boardArg = new Argument<int>("board") { Description = "The board number to remove." };
        var cmd = new Command("remove", "Remove a board number.");
        cmd.Arguments.Add(boardArg);
        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var handler = services.GetRequiredService<RemoveAsrlBoardCommandHandler>();
                var logger = services.GetRequiredService<ILogger<RemoveAsrlBoardCommandHandler>>();
                var board = parseResult.GetValue(boardArg);
                var result = await handler.HandleAsync(new RemoveAsrlBoardCommand(board), ct);
                return result switch
                {
                    Result<Unit, RemoveAsrlBoardError>.Ok => ServerCommand.Ok(
                        $"removed ASRL{board}::INSTR"
                    ),
                    Result<Unit, RemoveAsrlBoardError>.Error { Err: RemoveAsrlBoardNotFound } e =>
                        ServerCommand.Log(
                            e.Err,
                            logger,
                            $"error: ASRL board {board} is not registered.",
                            ExitCodeMapper.DeviceError
                        ),
                    Result<Unit, RemoveAsrlBoardError>.Error e => ServerCommand.Log(
                        e.Err,
                        logger,
                        "error: failed to remove the ASRL board.",
                        ExitCodeMapper.ConfigurationError
                    ),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );
        return cmd;
    }

    /// <summary>Renders the listing as plain text for human consumption.</summary>
    public static int Render(AsrlBoardListing listing, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(writer);

        if (listing.Boards.IsEmpty)
        {
            writer.WriteLine("(no ASRL boards registered)");
            return ExitCodeMapper.Success;
        }
        foreach (var entry in listing.Boards)
        {
            var absent = entry.PortPresent ? string.Empty : " (not present)";
            writer.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"ASRL{entry.Registration.Board}::INSTR -> {entry.Registration.Port}{absent}"
                )
            );
        }
        return ExitCodeMapper.Success;
    }

    /// <summary>
    /// Renders the listing as the JSON emitted with <c>--json</c>:
    /// <c>{"boards":[{"board":1,"port":"/dev/ttyUSB0","present":true}]}</c>.
    /// </summary>
    public static string RenderJson(AsrlBoardListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteStartArray("boards");
            foreach (var entry in listing.Boards)
            {
                json.WriteStartObject();
                json.WriteNumber("board", entry.Registration.Board);
                json.WriteString("port", entry.Registration.Port);
                json.WriteBoolean("present", entry.PortPresent);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static int ReportAdded(AsrlBoardAdded added)
    {
        var registration = added.Registration;
        Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"registered ASRL{registration.Board}::INSTR as {registration.Port}"
            )
        );
        if (added.PortMissing)
        {
            Console.Error.WriteLine(
                $"warning: {registration.Port} is not present now; it is registered anyway."
            );
        }
        return ExitCodeMapper.Success;
    }

    private static string AddFailureMessage(AddAsrlBoardError error) =>
        error switch
        {
            AddAsrlBoardInvalid i => string.Create(
                CultureInfo.InvariantCulture,
                $"error: board {i.Board} and port '{i.Port}' cannot form an ASRL resource; use a board of 0 or more and a port name that is not only digits."
            ),
            AddAsrlBoardTaken t => string.Create(
                CultureInfo.InvariantCulture,
                $"error: ASRL board {t.Board} is already registered; remove it first."
            ),
            AddAsrlPortTaken p => string.Create(
                CultureInfo.InvariantCulture,
                $"error: {p.Port} is already registered as ASRL board {p.Board}."
            ),
            _ => "error: failed to register the ASRL board.",
        };

    private static int AddExitCode(AddAsrlBoardError error) =>
        error switch
        {
            AddAsrlBoardInvalid => ExitCodeMapper.UsageError,
            AddAsrlBoardTaken or AddAsrlPortTaken => ExitCodeMapper.DeviceError,
            _ => ExitCodeMapper.ConfigurationError,
        };
}
