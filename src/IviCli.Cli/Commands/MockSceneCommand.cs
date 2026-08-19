using System.CommandLine;
using IviCli.Application.Logging;
using IviCli.Application.Mock;
using IviCli.Domain;
using IviCli.Domain.Mock;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>
/// Builds the <c>ivicli mock scene ...</c> subcommand tree
/// (ADR 0026 §5 + §15). In v0.2.0 the <c>scene</c> verb operates on
/// <em>state nodes</em> — adding or removing a named scene inside a
/// scenario. Rules (match → action) are managed by the sibling
/// <see cref="MockRuleCommand"/>.
/// </summary>
public static class MockSceneCommand
{
    /// <summary>Returns the <c>scene</c> command, ready to attach under <c>scenario</c>.</summary>
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("scene", "Manage scene states within a scenario.");
        command.Subcommands.Add(BuildAdd(services));
        command.Subcommands.Add(BuildRemove(services));
        return command;
    }

    private static Command BuildAdd(IServiceProvider services)
    {
        var scenarioArg = new Argument<string>("scenario") { Description = "The owning scenario." };
        var sceneArg = new Argument<string>("scene") { Description = "The new scene's alias." };

        var cmd = new Command("add", "Add a new empty scene to a scenario.");
        cmd.Arguments.Add(scenarioArg);
        cmd.Arguments.Add(sceneArg);

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var scenario = parseResult.GetRequiredValue(scenarioArg);
                var scene = parseResult.GetRequiredValue(sceneArg);

                var handler = services.GetRequiredService<AddSceneCommandHandler>();
                var logger = services.GetRequiredService<ILogger<AddSceneCommandHandler>>();

                var result = await handler.HandleAsync(new AddSceneCommand(scenario, scene), ct);
                return result switch
                {
                    Result<MockScenario, AddSceneError>.Ok ok => Success(
                        $"scene '{scene}' added to {ok.Value.Name.Value}"
                    ),
                    Result<MockScenario, AddSceneError>.Error err => err.Err switch
                    {
                        AddSceneInvalidScenarioName n => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: invalid scenario name '{n.Raw}'.",
                            ExitCodeMapper.UsageError
                        ),
                        AddSceneInvalidSceneName n => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: invalid scene name '{n.Raw}'.",
                            ExitCodeMapper.UsageError
                        ),
                        AddSceneScenarioNotFound nf => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: scenario '{nf.Name.Value}' not found.",
                            ExitCodeMapper.DeviceError
                        ),
                        AddSceneAlreadyExists ae => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: scene '{ae.Scene.Value}' already exists in '{ae.Scenario.Value}'.",
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

    private static Command BuildRemove(IServiceProvider services)
    {
        var scenarioArg = new Argument<string>("scenario") { Description = "The owning scenario." };
        var sceneArg = new Argument<string>("scene") { Description = "The scene alias to remove." };

        var cmd = new Command("remove", "Remove a scene from a scenario by alias.");
        cmd.Arguments.Add(scenarioArg);
        cmd.Arguments.Add(sceneArg);

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var scenario = parseResult.GetRequiredValue(scenarioArg);
                var scene = parseResult.GetRequiredValue(sceneArg);

                var handler = services.GetRequiredService<RemoveSceneCommandHandler>();
                var logger = services.GetRequiredService<ILogger<RemoveSceneCommandHandler>>();

                var result = await handler.HandleAsync(new RemoveSceneCommand(scenario, scene), ct);
                return result switch
                {
                    Result<MockScenario, RemoveSceneError>.Ok ok => Success(
                        $"scene '{scene}' removed from {ok.Value.Name.Value}"
                    ),
                    Result<MockScenario, RemoveSceneError>.Error err => err.Err switch
                    {
                        RemoveSceneInvalidScenarioName n => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: invalid scenario name '{n.Raw}'.",
                            ExitCodeMapper.UsageError
                        ),
                        RemoveSceneInvalidSceneName n => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: invalid scene name '{n.Raw}'.",
                            ExitCodeMapper.UsageError
                        ),
                        RemoveSceneScenarioNotFound nf => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: scenario '{nf.Name.Value}' not found.",
                            ExitCodeMapper.DeviceError
                        ),
                        RemoveSceneNotFound nf => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: scene '{nf.Scene.Value}' not found in '{nf.Scenario.Value}'.",
                            ExitCodeMapper.UsageError
                        ),
                        RemoveSceneIsInitial ri => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: scene '{ri.Scene.Value}' is the initial scene of '{ri.Scenario.Value}'; cannot remove.",
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
        logger.LogIviError(error);
        Console.Error.WriteLine(userMessage);
        return exitCode;
    }
}
