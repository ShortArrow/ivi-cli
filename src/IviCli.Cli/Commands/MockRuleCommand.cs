using System.CommandLine;
using IviCli.Application.Mock;
using IviCli.Domain;
using IviCli.Domain.Mock;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>
/// Builds the <c>ivicli mock scenario rule ...</c> subcommand tree
/// (ADR 0026 §5 + §15, B0.2-4). In v0.2.0 the <c>rule</c> verb manages
/// (match → action) pairs inside a named scene; <c>--in &lt;scene&gt;</c>
/// targets a specific scene and <c>--transition-to &lt;scene&gt;</c>
/// makes the rule swap the FakeBackend's current scene after it fires.
/// </summary>
public static class MockRuleCommand
{
    /// <summary>Returns the <c>rule</c> command, ready to attach under <c>scenario</c>.</summary>
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("rule", "Manage rules within a scenario's scenes.");
        command.Subcommands.Add(BuildAdd(services));
        command.Subcommands.Add(BuildRemove(services));
        return command;
    }

    private static Command BuildAdd(IServiceProvider services)
    {
        var scenarioArg = new Argument<string>("scenario") { Description = "The owning scenario." };
        var inOpt = new Option<string?>("--in")
        {
            Description = "Target scene name. Defaults to the scenario's initial scene if omitted.",
        };
        var matchOpt = new Option<string>("--match")
        {
            Description = "The exact SCPI text the rule reacts to (required).",
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
        var transitionOpt = new Option<string?>("--transition-to")
        {
            Description =
                "After the rule fires, make this scene current. Target scene must exist in the scenario.",
        };
        var srqOpt = new Option<string?>("--srq")
        {
            Description =
                "Status byte to raise a service request with when the rule fires (0..255, decimal or 0x hex).",
        };

        var cmd = new Command("add", "Append a rule to a scene inside a scenario.");
        cmd.Arguments.Add(scenarioArg);
        cmd.Options.Add(inOpt);
        cmd.Options.Add(matchOpt);
        cmd.Options.Add(respondOpt);
        cmd.Options.Add(ackOpt);
        cmd.Options.Add(failOpt);
        cmd.Options.Add(failDetailOpt);
        cmd.Options.Add(transitionOpt);
        cmd.Options.Add(srqOpt);

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var scenario = parseResult.GetRequiredValue(scenarioArg);
                var inScene = parseResult.GetValue(inOpt);
                var match = parseResult.GetRequiredValue(matchOpt);
                var respond = parseResult.GetValue(respondOpt);
                var ack = parseResult.GetValue(ackOpt);
                var fail = parseResult.GetValue(failOpt);
                var failDetail = parseResult.GetValue(failDetailOpt);
                var transition = parseResult.GetValue(transitionOpt);
                var srqRaw = parseResult.GetValue(srqOpt);
                byte? srq = null;
                if (srqRaw is { Length: > 0 })
                {
                    if (ParseStatusByte(srqRaw) is not { } parsedSrq)
                    {
                        Console.Error.WriteLine(
                            "error: --srq must be an integer 0..255 (decimal or 0x hex)."
                        );
                        return ExitCodeMapper.UsageError;
                    }
                    srq = parsedSrq;
                }

                var handler = services.GetRequiredService<AddRuleCommandHandler>();
                var logger = services.GetRequiredService<ILogger<AddRuleCommandHandler>>();

                var result = await handler.HandleAsync(
                    new AddRuleCommand(
                        scenario,
                        inScene,
                        match,
                        respond,
                        ack,
                        fail,
                        failDetail,
                        transition,
                        srq
                    ),
                    ct
                );
                return result switch
                {
                    Result<MockScenario, AddRuleError>.Ok ok => Success(
                        $"rule added to {ok.Value.Name.Value}/{inScene ?? ok.Value.InitialScene.Value}"
                    ),
                    Result<MockScenario, AddRuleError>.Error err => err.Err switch
                    {
                        AddRuleInvalidScenarioName n => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: invalid scenario name '{n.Raw}'.",
                            ExitCodeMapper.UsageError
                        ),
                        AddRuleInvalidSceneName n => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: invalid scene name '{n.Raw}'.",
                            ExitCodeMapper.UsageError
                        ),
                        AddRuleInvalidMatch => LogAndUserError(
                            err.Err,
                            logger,
                            "error: --match must be a non-empty SCPI string.",
                            ExitCodeMapper.UsageError
                        ),
                        AddRuleActionAmbiguous => LogAndUserError(
                            err.Err,
                            logger,
                            "error: specify exactly one of --respond, --ack, or --fail.",
                            ExitCodeMapper.UsageError
                        ),
                        AddRuleScenarioNotFound nf => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: scenario '{nf.Name.Value}' not found.",
                            ExitCodeMapper.DeviceError
                        ),
                        AddRuleSceneNotFound nf => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: scene '{nf.Scene.Value}' not found in '{nf.Scenario.Value}' (use `scene add` first).",
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
        var indexArg = new Argument<int>("index")
        {
            Description = "1-based rule index inside the target scene.",
        };
        var inOpt = new Option<string?>("--in")
        {
            Description = "Target scene name. Defaults to the scenario's initial scene if omitted.",
        };

        var cmd = new Command("remove", "Remove a rule from a scene by 1-based index.");
        cmd.Arguments.Add(scenarioArg);
        cmd.Arguments.Add(indexArg);
        cmd.Options.Add(inOpt);

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var scenario = parseResult.GetRequiredValue(scenarioArg);
                var index = parseResult.GetRequiredValue(indexArg);
                var inScene = parseResult.GetValue(inOpt);

                var handler = services.GetRequiredService<RemoveRuleCommandHandler>();
                var logger = services.GetRequiredService<ILogger<RemoveRuleCommandHandler>>();

                var result = await handler.HandleAsync(
                    new RemoveRuleCommand(scenario, inScene, index),
                    ct
                );
                return result switch
                {
                    Result<MockScenario, RemoveRuleError>.Ok ok => Success(
                        $"rule removed from {ok.Value.Name.Value}/{inScene ?? ok.Value.InitialScene.Value}"
                    ),
                    Result<MockScenario, RemoveRuleError>.Error err => err.Err switch
                    {
                        RemoveRuleInvalidScenarioName n => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: invalid scenario name '{n.Raw}'.",
                            ExitCodeMapper.UsageError
                        ),
                        RemoveRuleInvalidSceneName n => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: invalid scene name '{n.Raw}'.",
                            ExitCodeMapper.UsageError
                        ),
                        RemoveRuleScenarioNotFound nf => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: scenario '{nf.Name.Value}' not found.",
                            ExitCodeMapper.DeviceError
                        ),
                        RemoveRuleSceneNotFound nf => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: scene '{nf.Scene.Value}' not found in '{nf.Scenario.Value}'.",
                            ExitCodeMapper.UsageError
                        ),
                        RemoveRuleIndexOutOfRange ix => LogAndUserError(
                            err.Err,
                            logger,
                            $"error: rule index {ix.Index} out of range (1..{ix.Available}).",
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

    /// <summary>
    /// Reads a status byte written the way an instrument manual writes
    /// one — <c>96</c> or <c>0x60</c> — returning <see langword="null"/>
    /// for anything that is not a byte.
    /// </summary>
    private static byte? ParseStatusByte(string raw)
    {
        var isHex = raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        var digits = isHex ? raw[2..] : raw;
        var style = isHex
            ? System.Globalization.NumberStyles.HexNumber
            : System.Globalization.NumberStyles.None;
        return byte.TryParse(
            digits,
            style,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value
        )
            ? value
            : null;
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
