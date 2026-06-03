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
        command.Subcommands.Add(BuildRecord(services));
        command.Subcommands.Add(BuildImport(services));
        return command;
    }

    private static Command BuildImport(IServiceProvider services)
    {
        var pathArg = new Argument<string>("path")
        {
            Description = "Path to the NDJSON capture file (IVICLI_CAPTURE output).",
        };
        var nameOpt = new Option<string>("--name")
        {
            Description = "Scenario name to store the imported scenes under.",
            Required = true,
        };
        var deviceOpt = new Option<string?>("--device")
        {
            Description =
                "Device alias filter when the capture covers multiple devices (required in that case).",
        };
        var forceOpt = new Option<bool>("--force")
        {
            Description = "Overwrite an existing scenario with the same name.",
        };

        var cmd = new Command(
            "import",
            "Convert an NDJSON capture into a stored mock scenario (ADR 0033)."
        );
        cmd.Arguments.Add(pathArg);
        cmd.Options.Add(nameOpt);
        cmd.Options.Add(deviceOpt);
        cmd.Options.Add(forceOpt);

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var path = parseResult.GetRequiredValue(pathArg);
                var name = parseResult.GetRequiredValue(nameOpt);
                var device = parseResult.GetValue(deviceOpt);
                var force = parseResult.GetValue(forceOpt);

                var handler =
                    services.GetRequiredService<ImportScenarioFromTrafficCommandHandler>();
                var logger = services.GetRequiredService<
                    ILogger<ImportScenarioFromTrafficCommandHandler>
                >();
                var result = await handler.HandleAsync(
                    new ImportScenarioFromTrafficCommand(path, name, device, force),
                    ct
                );
                return result switch
                {
                    Result<ImportSummary, ImportTrafficError>.Ok ok => ImportOk(ok.Value),
                    Result<ImportSummary, ImportTrafficError>.Error err => ImportFail(
                        err.Err,
                        logger
                    ),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );
        return cmd;
    }

    private static int ImportOk(ImportSummary summary)
    {
        Console.WriteLine(
            $"imported '{summary.Name.Value}' with {summary.Scenes} scene(s) for device '{summary.Device}'"
        );
        return ExitCodeMapper.Success;
    }

    private static int ImportFail(ImportTrafficError error, ILogger logger)
    {
        logger.Log(
            Logging.SerilogConfiguration.ToLogLevel(error.Severity),
            error.Cause,
            error.Message,
            error.LogArgs.ToArray()
        );
        Console.Error.WriteLine(
            error switch
            {
                ImportTrafficInvalidName n => $"error: invalid scenario name '{n.Raw}'.",
                ImportTrafficInvalidDevice d => $"error: invalid device alias '{d.Raw}'.",
                ImportTrafficIoFailure io => $"error: cannot read capture '{io.Path}': {io.Reason}",
                ImportTrafficConvert { Inner: ConvertTrafficMultipleDevices md } =>
                    "error: capture covers multiple devices ("
                        + string.Join(", ", md.Devices)
                        + "); rerun with --device to pick one.",
                ImportTrafficConvert { Inner: ConvertTrafficNoScenes ns } => ns.DeviceFilter is null
                    ? "error: capture contained no replayable Write/Query events."
                    : $"error: no Write/Query events for device '{ns.DeviceFilter}'.",
                ImportTrafficStoreFailure s =>
                    $"error: scenario store rejected save: {s.Inner.Message}",
                _ => "error: import failed.",
            }
        );
        return error switch
        {
            ImportTrafficInvalidName or ImportTrafficInvalidDevice or ImportTrafficIoFailure =>
                ExitCodeMapper.UsageError,
            ImportTrafficConvert { Inner: ConvertTrafficMultipleDevices } =>
                ExitCodeMapper.UsageError,
            ImportTrafficStoreFailure => ExitCodeMapper.ConfigurationError,
            _ => ExitCodeMapper.GenericFailure,
        };
    }

    private static Command BuildRecord(IServiceProvider services)
    {
        var nameArg = new Argument<string>("name") { Description = "Scenario to record into." };
        var scriptOpt = new Option<string>("--from-script")
        {
            Description = "Script file whose queries/writes will be captured (ADR 0027 §4).",
            Required = true,
        };
        var deviceOpt = new Option<string?>("--device")
        {
            Description = "Target device alias (defaults to session-current).",
        };

        var cmd = new Command(
            "record",
            "Replay a script against the active backend and record observed traffic into a scenario."
        );
        cmd.Arguments.Add(nameArg);
        cmd.Options.Add(scriptOpt);
        cmd.Options.Add(deviceOpt);

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var name = parseResult.GetRequiredValue(nameArg);
                var scriptPath = parseResult.GetRequiredValue(scriptOpt);
                var device = parseResult.GetValue(deviceOpt);

                if (!File.Exists(scriptPath))
                {
                    Console.Error.WriteLine($"error: script file not found: {scriptPath}");
                    return ExitCodeMapper.UsageError;
                }
                var source = await File.ReadAllTextAsync(scriptPath, ct);

                var handler = services.GetRequiredService<RecordScenarioCommandHandler>();
                var logger = services.GetRequiredService<ILogger<RecordScenarioCommandHandler>>();
                var result = await handler.HandleAsync(
                    new RecordScenarioCommand(name, device, source),
                    ct
                );
                return result switch
                {
                    Result<RecordScenarioReport, RecordScenarioError>.Ok ok => RecordOk(ok.Value),
                    Result<RecordScenarioReport, RecordScenarioError>.Error err => RecordFail(
                        err.Err,
                        logger
                    ),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );
        return cmd;
    }

    private static int RecordOk(RecordScenarioReport report)
    {
        Console.WriteLine(
            $"recorded {report.ScenesRecorded} scene(s) into scenario '{report.ScenarioName.Value}'"
        );
        return ExitCodeMapper.Success;
    }

    private static int RecordFail(RecordScenarioError error, ILogger logger)
    {
        logger.Log(
            Logging.SerilogConfiguration.ToLogLevel(error.Severity),
            error.Cause,
            error.Message,
            error.LogArgs.ToArray()
        );
        Console.Error.WriteLine(
            error switch
            {
                RecordScenarioInvalidName n => $"error: invalid scenario name '{n.Raw}'.",
                RecordScenarioParseFailure p => $"error: script parse failed ({p.Inner.Message}).",
                RecordScenarioInvalidDeviceName d => $"error: invalid device name '{d.Raw}'.",
                RecordScenarioNoTarget =>
                    "error: no current device. Use `visa use <name>` first or pass --device.",
                RecordScenarioUnknownDevice u => $"error: no device named '{u.Name.Value}'.",
                RecordScenarioTransportFailure => "error: transport failure while recording.",
                RecordScenarioStoreFailure => "error: scenario storage failed.",
                _ => "error: scenario recording failed.",
            }
        );
        return error switch
        {
            RecordScenarioInvalidName
            or RecordScenarioParseFailure
            or RecordScenarioInvalidDeviceName
            or RecordScenarioNoTarget => ExitCodeMapper.UsageError,
            RecordScenarioUnknownDevice => ExitCodeMapper.DeviceError,
            RecordScenarioTransportFailure => ExitCodeMapper.TransportError,
            RecordScenarioStoreFailure => ExitCodeMapper.ConfigurationError,
            _ => ExitCodeMapper.GenericFailure,
        };
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
        // v0.1.x compat output: flatten every scene's rules into a
        // single ordered list. v0.2.0's multi-scene `show` rendering
        // lands in a follow-up batch (issue #26 §"Implementation plan"
        // — B0.2-4).
        var rules = scenario.Scenes.SelectMany(s => s.Rules).ToList();
        if (rules.Count == 0)
        {
            Console.WriteLine("scenes: (none)");
            return ExitCodeMapper.Success;
        }
        Console.WriteLine("scenes:");
        for (var i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            var action = r.Action switch
            {
                RuleAction.Respond resp => $"respond \"{resp.Text}\"",
                RuleAction.Ack => "ack",
                RuleAction.Fail f => f.Detail is null
                    ? $"fail {f.Variant}"
                    : $"fail {f.Variant} ({f.Detail})",
                _ => "?",
            };
            Console.WriteLine($"  [{i + 1}] {r.Match} -> {action}");
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
