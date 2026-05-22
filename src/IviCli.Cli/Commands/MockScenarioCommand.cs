using System.CommandLine;
using IviCli.Application.Mock;
using IviCli.Domain;
using IviCli.Domain.Mock;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>
/// Builds the <c>ivicli mock scenario ...</c> subcommand tree per ADR 0026 §5.
/// </summary>
public static class MockScenarioCommand
{
    /// <summary>Returns the full <c>scenario</c> command, ready to attach under <c>mock</c>.</summary>
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("scenario", "Manage mock scenarios.");
        command.Subcommands.Add(BuildList(services));
        command.Subcommands.Add(BuildCreate(services));
        command.Subcommands.Add(BuildRemove(services));
        command.Subcommands.Add(BuildShow(services));
        command.Subcommands.Add(BuildActivate(services));
        command.Subcommands.Add(BuildDeactivate(services));
        command.Subcommands.Add(MockSceneCommand.Build(services));
        return command;
    }

    private static Command BuildList(IServiceProvider services)
    {
        var jsonOpt = new Option<bool>("--json") { Description = "Emit machine-readable JSON." };
        var cmd = new Command("list", "List every scenario.");
        cmd.Options.Add(jsonOpt);

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var json = parseResult.GetValue(jsonOpt);
                var handler = services.GetRequiredService<ListScenariosQueryHandler>();
                var logger = services.GetRequiredService<ILogger<ListScenariosQueryHandler>>();

                var result = await handler.HandleAsync(new ListScenariosQuery(), ct);
                return result switch
                {
                    Result<ScenarioListing, ListScenariosError>.Ok ok => RenderList(ok.Value, json),
                    Result<ScenarioListing, ListScenariosError>.Error err => LogAndFail(
                        err.Err,
                        logger,
                        "error: failed to list scenarios."
                    ),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );
        return cmd;
    }

    private static Command BuildCreate(IServiceProvider services)
    {
        var nameArg = new Argument<string>("name") { Description = "Scenario name." };
        var idnOpt = new Option<string?>("--idn")
        {
            Description = "Optional default *IDN? response for the scenario.",
        };

        var cmd = new Command("create", "Create an empty scenario.");
        cmd.Arguments.Add(nameArg);
        cmd.Options.Add(idnOpt);

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var name = parseResult.GetRequiredValue(nameArg);
                var idn = parseResult.GetValue(idnOpt);
                var handler = services.GetRequiredService<CreateScenarioCommandHandler>();
                var logger = services.GetRequiredService<ILogger<CreateScenarioCommandHandler>>();

                var result = await handler.HandleAsync(new CreateScenarioCommand(name, idn), ct);
                return result switch
                {
                    Result<ScenarioName, CreateScenarioError>.Ok ok => SuccessLine(
                        $"created scenario {ok.Value.Value}"
                    ),
                    Result<ScenarioName, CreateScenarioError>.Error err => err.Err switch
                    {
                        CreateScenarioInvalidName n => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: invalid scenario name '{n.Raw}'.",
                            ExitCodeMapper.UsageError
                        ),
                        CreateScenarioAlreadyExists a => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: scenario '{a.Name.Value}' already exists.",
                            ExitCodeMapper.DeviceError
                        ),
                        _ => LogAndUserError(
                            err.Err,
                            logger,
                            "error: scenario storage failed.",
                            ExitCodeMapper.ConfigurationError
                        ),
                    },
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );
        return cmd;
    }

    private static Command BuildRemove(IServiceProvider services)
    {
        var nameArg = new Argument<string>("name") { Description = "Scenario name." };

        var cmd = new Command("remove", "Remove a scenario.");
        cmd.Arguments.Add(nameArg);

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var name = parseResult.GetRequiredValue(nameArg);
                var handler = services.GetRequiredService<RemoveScenarioCommandHandler>();
                var logger = services.GetRequiredService<ILogger<RemoveScenarioCommandHandler>>();

                var result = await handler.HandleAsync(new RemoveScenarioCommand(name), ct);
                return result switch
                {
                    Result<ScenarioName, RemoveScenarioError>.Ok ok => SuccessLine(
                        $"removed scenario {ok.Value.Value}"
                    ),
                    Result<ScenarioName, RemoveScenarioError>.Error err => err.Err switch
                    {
                        RemoveScenarioInvalidName n => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: invalid scenario name '{n.Raw}'.",
                            ExitCodeMapper.UsageError
                        ),
                        RemoveScenarioNotFound nf => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: scenario '{nf.Name.Value}' not found.",
                            ExitCodeMapper.DeviceError
                        ),
                        _ => LogAndUserError(
                            err.Err,
                            logger,
                            "error: scenario storage failed.",
                            ExitCodeMapper.ConfigurationError
                        ),
                    },
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );
        return cmd;
    }

    private static Command BuildShow(IServiceProvider services)
    {
        var nameArg = new Argument<string>("name") { Description = "Scenario name." };

        var cmd = new Command("show", "Show a scenario's scenes.");
        cmd.Arguments.Add(nameArg);

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var name = parseResult.GetRequiredValue(nameArg);
                var handler = services.GetRequiredService<ShowScenarioQueryHandler>();
                var logger = services.GetRequiredService<ILogger<ShowScenarioQueryHandler>>();

                var result = await handler.HandleAsync(new ShowScenarioQuery(name), ct);
                return result switch
                {
                    Result<MockScenario, ShowScenarioError>.Ok ok => RenderShow(ok.Value),
                    Result<MockScenario, ShowScenarioError>.Error err => err.Err switch
                    {
                        ShowScenarioInvalidName n => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: invalid scenario name '{n.Raw}'.",
                            ExitCodeMapper.UsageError
                        ),
                        ShowScenarioNotFound nf => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: scenario '{nf.Name.Value}' not found.",
                            ExitCodeMapper.DeviceError
                        ),
                        _ => LogAndUserError(
                            err.Err,
                            logger,
                            "error: scenario storage failed.",
                            ExitCodeMapper.ConfigurationError
                        ),
                    },
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );
        return cmd;
    }

    private static Command BuildActivate(IServiceProvider services)
    {
        var nameArg = new Argument<string>("name") { Description = "Scenario name." };

        var cmd = new Command("activate", "Make a scenario the active mock.");
        cmd.Arguments.Add(nameArg);

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var name = parseResult.GetRequiredValue(nameArg);
                var handler = services.GetRequiredService<ActivateScenarioCommandHandler>();
                var logger = services.GetRequiredService<ILogger<ActivateScenarioCommandHandler>>();

                var result = await handler.HandleAsync(new ActivateScenarioCommand(name), ct);
                return result switch
                {
                    Result<ScenarioName, ActivateScenarioError>.Ok ok => SuccessLine(
                        $"activated scenario {ok.Value.Value}"
                    ),
                    Result<ScenarioName, ActivateScenarioError>.Error err => err.Err switch
                    {
                        ActivateScenarioInvalidName n => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: invalid scenario name '{n.Raw}'.",
                            ExitCodeMapper.UsageError
                        ),
                        ActivateScenarioNotFound nf => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: scenario '{nf.Name.Value}' not found.",
                            ExitCodeMapper.DeviceError
                        ),
                        _ => LogAndUserError(
                            err.Err,
                            logger,
                            "error: scenario / session storage failed.",
                            ExitCodeMapper.ConfigurationError
                        ),
                    },
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );
        return cmd;
    }

    private static Command BuildDeactivate(IServiceProvider services)
    {
        var cmd = new Command("deactivate", "Clear any active mock scenario.");
        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var handler = services.GetRequiredService<DeactivateScenarioCommandHandler>();
                var logger = services.GetRequiredService<
                    ILogger<DeactivateScenarioCommandHandler>
                >();

                var result = await handler.HandleAsync(new DeactivateScenarioCommand(), ct);
                return result switch
                {
                    Result<Unit, ActivateScenarioError>.Ok => SuccessLine("scenario deactivated"),
                    Result<Unit, ActivateScenarioError>.Error err => LogAndUserError(
                        err.Err,
                        logger,
                        "error: session storage failed.",
                        ExitCodeMapper.ConfigurationError
                    ),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );
        return cmd;
    }

    private static int RenderList(ScenarioListing listing, bool emitJson)
    {
        if (emitJson)
        {
            Console.Write("{\"scenarios\":[");
            for (var i = 0; i < listing.Names.Length; i++)
            {
                if (i > 0)
                {
                    Console.Write(",");
                }
                Console.Write($"\"{listing.Names[i].Value}\"");
            }
            Console.WriteLine("]}");
        }
        else
        {
            if (listing.Names.IsEmpty)
            {
                Console.WriteLine("(no scenarios)");
            }
            else
            {
                foreach (var n in listing.Names)
                {
                    Console.WriteLine(n.Value);
                }
            }
        }
        return ExitCodeMapper.Success;
    }

    private static int RenderShow(MockScenario scenario)
    {
        Console.WriteLine($"name: {scenario.Name.Value}");
        if (scenario.IdnDefault is { } idn)
        {
            Console.WriteLine($"idn:  {idn}");
        }
        if (scenario.Scenes.IsEmpty)
        {
            Console.WriteLine("scenes: (none)");
            return ExitCodeMapper.Success;
        }
        Console.WriteLine("scenes:");
        for (var i = 0; i < scenario.Scenes.Length; i++)
        {
            var s = scenario.Scenes[i];
            var action = s.Action switch
            {
                SceneAction.Respond r => $"respond \"{r.Text}\"",
                SceneAction.Ack => "ack",
                SceneAction.Fail f => f.Detail is null
                    ? $"fail {f.Variant}"
                    : $"fail {f.Variant} ({f.Detail})",
                _ => "?",
            };
            Console.WriteLine($"  [{i + 1}] {s.Match} -> {action}");
        }
        return ExitCodeMapper.Success;
    }

    private static int SuccessLine(string message)
    {
        Console.WriteLine(message);
        return ExitCodeMapper.Success;
    }

    private static int LogAndFail(IviError error, ILogger logger, string userMessage)
    {
        logger.Log(
            Logging.SerilogConfiguration.ToLogLevel(error.Severity),
            error.Cause,
            error.Message,
            error.LogArgs.ToArray()
        );
        Console.Error.WriteLine(userMessage);
        return ExitCodeMapper.ConfigurationError;
    }

    private static int LogAndUserError(
        IviError error,
        ILogger logger,
        string userMessage,
        int exitCode
    )
    {
        logger.Log(
            Logging.SerilogConfiguration.ToLogLevel(error.Severity),
            error.Cause,
            error.Message,
            error.LogArgs.ToArray()
        );
        Console.Error.WriteLine(userMessage);
        return exitCode;
    }
}
