using System.CommandLine;
using IviCli.Application.Mock;
using IviCli.Domain;
using IviCli.Domain.Mock;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>
/// Builds the <c>ivicli mock scenario scene ...</c> subcommand tree
/// (ADR 0026 §5). The scenario name is the first argument to each scene
/// subcommand; listing scenes is delegated to <c>scenario show</c>.
/// </summary>
public static class MockSceneCommand
{
    /// <summary>Returns the <c>scene</c> command, ready to attach under <c>scenario</c>.</summary>
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("scene", "Manage scenes within a scenario.");
        command.Subcommands.Add(BuildAdd(services));
        command.Subcommands.Add(BuildRemove(services));
        return command;
    }

    private static Command BuildAdd(IServiceProvider services)
    {
        var scenarioArg = new Argument<string>("scenario")
        {
            Description = "The scenario to add the scene to.",
        };
        var matchOpt = new Option<string>("--match")
        {
            Description = "The exact SCPI text the scene reacts to (required).",
            Required = true,
        };
        var respondOpt = new Option<string?>("--respond")
        {
            Description = "Textual response (legal for queries).",
        };
        var ackOpt = new Option<bool>("--ack")
        {
            Description = "Acknowledge with no response (legal for writes).",
        };
        var failOpt = new Option<string?>("--fail")
        {
            Description =
                "Surface a canned BackendError. Variant tags: transport_timeout, transport_disconnected.",
        };
        var failDetailOpt = new Option<string?>("--fail-detail")
        {
            Description = "Detail payload for the fail variant (e.g. timeout ms).",
        };

        var cmd = new Command("add", "Append a scene to a scenario.");
        cmd.Arguments.Add(scenarioArg);
        cmd.Options.Add(matchOpt);
        cmd.Options.Add(respondOpt);
        cmd.Options.Add(ackOpt);
        cmd.Options.Add(failOpt);
        cmd.Options.Add(failDetailOpt);

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var scenario = parseResult.GetRequiredValue(scenarioArg);
                var match = parseResult.GetRequiredValue(matchOpt);
                var respond = parseResult.GetValue(respondOpt);
                var ack = parseResult.GetValue(ackOpt);
                var fail = parseResult.GetValue(failOpt);
                var failDetail = parseResult.GetValue(failDetailOpt);

                var handler = services.GetRequiredService<AddSceneCommandHandler>();
                var logger = services.GetRequiredService<ILogger<AddSceneCommandHandler>>();

                var result = await handler.HandleAsync(
                    new AddSceneCommand(scenario, match, respond, ack, fail, failDetail),
                    ct
                );
                return result switch
                {
                    Result<MockScenario, AddSceneError>.Ok ok => Success(
                        $"scene #{ok.Value.Scenes.Length} added to {ok.Value.Name.Value}"
                    ),
                    Result<MockScenario, AddSceneError>.Error err => err.Err switch
                    {
                        AddSceneInvalidScenarioName n => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: invalid scenario name '{n.Raw}'.",
                            ExitCodeMapper.UsageError
                        ),
                        AddSceneInvalidMatch => LogAndUserError(
                            err.Err,
                            logger,
                            "error: --match must be a non-empty SCPI string.",
                            ExitCodeMapper.UsageError
                        ),
                        AddSceneActionAmbiguous => LogAndUserError(
                            err.Err,
                            logger,
                            "error: specify exactly one of --respond, --ack, or --fail.",
                            ExitCodeMapper.UsageError
                        ),
                        AddSceneScenarioNotFound nf => LogAndUserError(
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

    private static Command BuildRemove(IServiceProvider services)
    {
        var scenarioArg = new Argument<string>("scenario")
        {
            Description = "The scenario to remove the scene from.",
        };
        var indexArg = new Argument<int>("index")
        {
            Description = "1-based scene index as reported by `scenario show`.",
        };

        var cmd = new Command("remove", "Remove a scene from a scenario by 1-based index.");
        cmd.Arguments.Add(scenarioArg);
        cmd.Arguments.Add(indexArg);

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var scenario = parseResult.GetRequiredValue(scenarioArg);
                var index = parseResult.GetRequiredValue(indexArg);

                var handler = services.GetRequiredService<RemoveSceneCommandHandler>();
                var logger = services.GetRequiredService<ILogger<RemoveSceneCommandHandler>>();

                var result = await handler.HandleAsync(new RemoveSceneCommand(scenario, index), ct);
                return result switch
                {
                    Result<MockScenario, RemoveSceneError>.Ok ok => Success(
                        $"scene removed from {ok.Value.Name.Value}"
                    ),
                    Result<MockScenario, RemoveSceneError>.Error err => err.Err switch
                    {
                        RemoveSceneInvalidScenarioName n => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: invalid scenario name '{n.Raw}'.",
                            ExitCodeMapper.UsageError
                        ),
                        RemoveSceneScenarioNotFound nf => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: scenario '{nf.Name.Value}' not found.",
                            ExitCodeMapper.DeviceError
                        ),
                        RemoveSceneIndexOutOfRange ix => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: index {ix.Index} out of range (1..{ix.Available}).",
                            ExitCodeMapper.UsageError
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

    private static int Success(string message)
    {
        Console.WriteLine(message);
        return ExitCodeMapper.Success;
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
