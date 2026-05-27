using System.CommandLine;
using IviCli.Application;
using IviCli.Application.Mock;
using IviCli.Application.Session;
using IviCli.Backends.Fake;
using IviCli.Backends.HiSlip;
using IviCli.Backends.Local;
using IviCli.Backends.Socket;
using IviCli.Backends.Vxi11;
using IviCli.Cli.Commands;
using IviCli.Cli.Logging;
using IviCli.Cli.Paths;
using IviCli.Domain;
using IviCli.Domain.Mock;
using IviCli.Domain.Session;
using IviCli.Infrastructure;
using IviCli.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace IviCli.Cli;

/// <summary>
/// Composition root for the ivi-cli command-line entry point. The only
/// place in the codebase permitted to know about Serilog, the DI container,
/// and System.CommandLine plumbing (per ADRs 0010 §8, 0011 §13, 0023 §5).
/// </summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Resolve global verbosity / format flags before constructing the logger.
        var verbosity = ResolveVerbosity(args, out var quiet);
        var consoleJson = ResolveJsonFormat(args);
        var logFileOverride = ResolveLogFileOverride(args);

        Log.Logger = SerilogConfiguration.Build(
            new SerilogConfiguration.Options(
                MinimumLevel: verbosity,
                ConsoleMinimumLevel: quiet ? LogEventLevel.Warning : verbosity,
                ConsoleJsonFormat: consoleJson,
                LogFileOverride: logFileOverride
            )
        );

        try
        {
            var configPath = IviPaths.ResolveConfigPath();

            var services = new ServiceCollection();
            services.AddLogging(b => b.AddSerilog(Log.Logger, dispose: false));
            services.AddIviCliApplication();
            services.AddIviCliMock();
            services.AddIviCliServers();
            services.AddIviCliGatewayServers();
            services.AddIviCliInfrastructure(configPath);
            services.AddIviCliServerProcessRegistry(IviPaths.ResolveServerStateDirectory());
            services.AddIviCliScenarioStore(configPath);
            services.AddIviCliBackendsFake();
            services.AddIviCliBackendsSocket();
            services.AddIviCliBackendsHiSlip();
            services.AddIviCliBackendsVxi11();
            services.AddIviCliBackendsLocal();
            // Dynamic-completion plumbing for the `__complete` verb.
            services.AddSingleton<IviCli.Cli.Completion.CompletionRegistry>();
            services.AddSingleton<
                IviCli.Cli.Completion.IDynamicCompleter,
                IviCli.Cli.Completion.Completers.DeviceNameCompleter
            >();
            services.AddSingleton<
                IviCli.Cli.Completion.IDynamicCompleter,
                IviCli.Cli.Completion.Completers.ServerNameCompleter
            >();
            services.AddSingleton<
                IviCli.Cli.Completion.IDynamicCompleter,
                IviCli.Cli.Completion.Completers.ScenarioNameCompleter
            >();
            services.AddSingleton<
                IviCli.Cli.Completion.IDynamicCompleter,
                IviCli.Cli.Completion.Completers.ScpiCommandCompleter
            >();
            // Composition-root wiring of DefaultBackendFactory: route TCPIP
            // HiSLIP -> HiSlipBackend, SOCKET-style TCPIP -> SocketBackend,
            // other TCPIP / USB / GPIB -> LocalBackend, fallback -> FakeBackend.
            // When IVICLI_REPLAY=<scenario> is set, swap the fallback for a
            // ReplayBackend so device traffic is served from the recorded
            // scenario instead of hitting any live transport (ADR 0028 §2).
            services.AddSingleton<IviCli.Application.Backends.IBackendFactory>(sp =>
            {
                IviCli.Application.Backends.IIviBackend fallback =
                    sp.GetRequiredService<IviCli.Backends.Fake.FakeBackend>();
                var replayName = Environment.GetEnvironmentVariable("IVICLI_REPLAY");
                if (!string.IsNullOrEmpty(replayName))
                {
                    var store = sp.GetRequiredService<IScenarioStore>();
                    if (
                        ScenarioName.From(replayName) is Result<ScenarioName, ScenarioNameError>.Ok
                        {
                            Value: var sn
                        }
                    )
                    {
                        var loaded = store
                            .LoadAsync(sn, CancellationToken.None)
                            .GetAwaiter()
                            .GetResult();
                        if (loaded is Result<MockScenario, ScenarioStoreError>.Ok { Value: var sc })
                        {
                            fallback = new IviCli.Backends.Replay.ReplayBackend(sc);
                        }
                        else
                        {
                            Log.Logger.Warning(
                                "IVICLI_REPLAY={Name}: scenario could not be loaded; falling back to FakeBackend",
                                replayName
                            );
                        }
                    }
                    else
                    {
                        Log.Logger.Warning(
                            "IVICLI_REPLAY={Name}: invalid scenario name; falling back to FakeBackend",
                            replayName
                        );
                    }
                }
                IviCli.Application.Backends.IBackendFactory factory =
                    new IviCli.Infrastructure.Backends.DefaultBackendFactory(
                        fallbackBackend: fallback,
                        localBackend: sp.GetRequiredService<IviCli.Backends.Local.LocalBackend>(),
                        hislipBackend: sp.GetRequiredService<IviCli.Backends.HiSlip.HiSlipBackend>(),
                        socketBackend: sp.GetRequiredService<IviCli.Backends.Socket.SocketBackend>(),
                        vxi11Backend: sp.GetRequiredService<IviCli.Backends.Vxi11.Vxi11Backend>()
                    );

                // IVICLI_CAPTURE=<path> wraps the factory so every backend op
                // streams into a NDJSON audit log (ADR 0031). Errors here fall
                // back to the null writer so the CLI never fails because the
                // capture sink misbehaves.
                var capturePath = Environment.GetEnvironmentVariable("IVICLI_CAPTURE");
                if (!string.IsNullOrWhiteSpace(capturePath))
                {
                    try
                    {
                        var resolved = Path.IsPathRooted(capturePath)
                            ? capturePath
                            : Path.Combine(IviPaths.ResolveLogDirectory(), capturePath);
                        var fileSystem =
                            sp.GetRequiredService<System.IO.Abstractions.IFileSystem>();
                        var realWriter = new IviCli.Infrastructure.Capture.NdjsonTrafficWriter(
                            fileSystem,
                            resolved
                        );
                        var backendLogger = sp.GetService<
                            ILogger<IviCli.Application.Backends.CapturingBackend>
                        >();
                        factory = new IviCli.Application.Backends.CapturingBackendFactory(
                            factory,
                            realWriter,
                            backendLogger
                        );
                        Log.Logger.Information("VISA traffic capture enabled → {Path}", resolved);
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Warning(
                            ex,
                            "IVICLI_CAPTURE={Path}: could not enable traffic capture; continuing without",
                            capturePath
                        );
                    }
                }
                return factory;
            });
            services.AddSingleton(
                new IviCli.Application.Diagnostics.DiagnoseHandlerOptions(
                    ConfigPath: configPath,
                    LogDirectory: IviPaths.ResolveLogDirectory()
                )
            );

            await using var provider = services.BuildServiceProvider();

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            // Activate any scenario named by IVICLI_SCENARIO (env wins) or
            // session.json (persisted) before parsing the command line, so
            // visa subcommands see the scenario on this same invocation.
            await ActivateScenarioIfRequested(provider, cts.Token);

            var root = BuildRoot(provider);
            return await root.Parse(args).InvokeAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("cancelled");
            return ExitCodeMapper.Cancelled;
        }
        catch (Exception ex)
        {
            Log.Logger.Fatal(ex, "Unhandled exception at composition root");
            Console.Error.WriteLine($"fatal: {ex.Message}");
            return ExitCodeMapper.GenericFailure;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static RootCommand BuildRoot(IServiceProvider services)
    {
        var visa = new Command("visa", "VISA transport / SCPI operations.");
        visa.Subcommands.Add(VisaAddCommand.Build(services));
        visa.Subcommands.Add(VisaRemoveCommand.Build(services));
        visa.Subcommands.Add(VisaListCommand.Build(services));
        visa.Subcommands.Add(VisaUseCommand.Build(services));
        visa.Subcommands.Add(VisaCurrentCommand.Build(services));
        visa.Subcommands.Add(VisaScanCommand.Build(services));
        visa.Subcommands.Add(VisaQueryCommand.Build(services));
        visa.Subcommands.Add(VisaWriteCommand.Build(services));
        visa.Subcommands.Add(VisaReadCommand.Build(services));
        visa.Subcommands.Add(VisaStatusCommand.Build(services));
        visa.Subcommands.Add(VisaScriptCommand.Build(services));
        visa.Subcommands.Add(VisaMonitorCommand.Build(services));
        visa.Subcommands.Add(VisaWatchCommand.Build(services));
        visa.Subcommands.Add(VisaLintCommand.Build(services));

        var mock = new Command("mock", "Manage mock-device behaviour for the Fake Backend.");
        mock.Subcommands.Add(MockScenarioCommand.Build(services));

        var root = new RootCommand(
            "ivi-cli: integrated CLI for managing, diagnosing, and operating VISA/IVI instruments."
        );
        root.Subcommands.Add(visa);
        root.Subcommands.Add(mock);
        root.Subcommands.Add(ServerCommand.Build(services));
        root.Subcommands.Add(DiagnoseCommand.Build(services));
        root.Subcommands.Add(CompletionCommand.Build());
        root.Subcommands.Add(CompleteCommand.Build(root, services));

        // Populate the dynamic-completion registry now that every verb
        // is attached. Each binding is "command + positional/option
        // slot → IDynamicCompleter Name"; the registry's Resolve uses
        // the same key from __complete.
        BindDynamicCompletion(root, services);

        return root;
    }

    private static void BindDynamicCompletion(RootCommand root, IServiceProvider services)
    {
        var registry = services.GetRequiredService<IviCli.Cli.Completion.CompletionRegistry>();
        var device = services
            .GetServices<IviCli.Cli.Completion.IDynamicCompleter>()
            .FirstOrDefault(c => c.Name == "device");
        var server = services
            .GetServices<IviCli.Cli.Completion.IDynamicCompleter>()
            .FirstOrDefault(c => c.Name == "server");
        var scenario = services
            .GetServices<IviCli.Cli.Completion.IDynamicCompleter>()
            .FirstOrDefault(c => c.Name == "scenario");
        if (device is null || server is null || scenario is null)
        {
            return;
        }

        // visa verbs that accept a device alias as the first positional
        var visa = root.Subcommands.First(c => c.Name == "visa");
        foreach (
            var name in new[]
            {
                "use",
                "remove",
                "current",
                "status",
                "query",
                "write",
                "read",
                "watch",
            }
        )
        {
            var sub = visa.Subcommands.FirstOrDefault(c => c.Name == name);
            if (sub is not null && sub.Arguments.Count > 0)
            {
                registry.Bind(sub, sub.Arguments[0].Name, device);
            }
        }
        // visa script / visa monitor: --device option
        foreach (var name in new[] { "script", "monitor" })
        {
            var sub = visa.Subcommands.FirstOrDefault(c => c.Name == name);
            if (sub is not null)
            {
                registry.Bind(sub, "device", device);
            }
        }

        // mock scenario activate / remove / show / record: scenario name positional
        var mock = root.Subcommands.First(c => c.Name == "mock");
        var scenarioCmd = mock.Subcommands.FirstOrDefault(c => c.Name == "scenario");
        if (scenarioCmd is not null)
        {
            foreach (var name in new[] { "activate", "remove", "show", "record" })
            {
                var sub = scenarioCmd.Subcommands.FirstOrDefault(c => c.Name == name);
                if (sub is not null && sub.Arguments.Count > 0)
                {
                    registry.Bind(sub, sub.Arguments[0].Name, scenario);
                }
            }
        }

        // server start / stop / log: server name positional
        var serverCmd = root.Subcommands.First(c => c.Name == "server");
        foreach (var name in new[] { "start", "stop", "log", "remove" })
        {
            var sub = serverCmd.Subcommands.FirstOrDefault(c => c.Name == name);
            if (sub is not null && sub.Arguments.Count > 0)
            {
                registry.Bind(sub, sub.Arguments[0].Name, server);
            }
        }
    }

    private static async Task ActivateScenarioIfRequested(
        IServiceProvider services,
        CancellationToken ct
    )
    {
        // Highest precedence: IVICLI_SCENARIO env var. Falls back to session.json.
        string? requested = Environment.GetEnvironmentVariable("IVICLI_SCENARIO");
        if (string.IsNullOrEmpty(requested))
        {
            var sessionStore = services.GetRequiredService<ISessionStore>();
            var sessionResult = await sessionStore.LoadAsync(ct);
            if (
                sessionResult is Result<SessionState, SessionStoreError>.Ok { Value: var session }
                && session.ActiveScenario is { } scenarioName
            )
            {
                requested = scenarioName.Value;
            }
        }
        if (string.IsNullOrEmpty(requested))
        {
            return;
        }
        if (
            ScenarioName.From(requested)
            is not Result<ScenarioName, ScenarioNameError>.Ok { Value: var name }
        )
        {
            Log.Logger.Warning("ignoring invalid IVICLI_SCENARIO value {Raw}", requested);
            return;
        }
        var store = services.GetRequiredService<IScenarioStore>();
        var loadResult = await store.LoadAsync(name, ct);
        if (loadResult is not Result<MockScenario, ScenarioStoreError>.Ok { Value: var scenario })
        {
            Log.Logger.Warning("could not load active scenario {Name}: ignored", requested);
            return;
        }
        var fake = services.GetRequiredService<FakeBackend>();
        fake.ActivateScenario(scenario);
    }

    private static LogEventLevel ResolveVerbosity(string[] args, out bool quiet)
    {
        var vvCount = 0;
        var vCount = 0;
        quiet = false;
        foreach (var a in args)
        {
            switch (a)
            {
                case "-vv":
                    vvCount++;
                    break;
                case "-v":
                case "--verbose":
                    vCount++;
                    break;
                case "-q":
                case "--quiet":
                    quiet = true;
                    break;
                default:
                    break;
            }
        }
        if (vvCount > 0)
        {
            return LogEventLevel.Verbose;
        }
        if (vCount > 0)
        {
            return LogEventLevel.Debug;
        }
        return LogEventLevel.Information;
    }

    private static bool ResolveJsonFormat(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--log-format" && i + 1 < args.Length)
            {
                return string.Equals(args[i + 1], "json", StringComparison.OrdinalIgnoreCase);
            }
            if (args[i].StartsWith("--log-format=", StringComparison.Ordinal))
            {
                return string.Equals(
                    args[i]["--log-format=".Length..],
                    "json",
                    StringComparison.OrdinalIgnoreCase
                );
            }
        }
        return false;
    }

    private static string? ResolveLogFileOverride(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--log-file" && i + 1 < args.Length)
            {
                return args[i + 1];
            }
            if (args[i].StartsWith("--log-file=", StringComparison.Ordinal))
            {
                return args[i]["--log-file=".Length..];
            }
        }
        return null;
    }
}
