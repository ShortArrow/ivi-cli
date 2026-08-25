using System.CommandLine;
using IviCli.Application;
using IviCli.Application.Mock;
using IviCli.Application.Session;
using IviCli.Backends.Fake;
using IviCli.Backends.HiSlip;
using IviCli.Backends.Local;
using IviCli.Backends.Lxi;
using IviCli.Backends.Socket;
using IviCli.Backends.Vxi11;
using IviCli.Cli.Commands;
using IviCli.Cli.Logging;
using IviCli.Cli.Paths;
using IviCli.Domain;
using IviCli.Domain.Devices;
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
            services.AddSingleton<TimeProvider>(TimeProvider.System);
            services.AddIviCliApplication();
            services.AddIviCliMock();
            services.AddIviCliServers();
            services.AddIviCliGatewayServers();
            services.AddIviCliInfrastructure(configPath);
            services.AddIviCliIviConfigurationStore();
            services.AddIviCliServerProcessRegistry(IviPaths.ResolveServerStateDirectory());

            // Eager config load for the OTel bootstrap (ADR 0040) — OTel
            // pipelines must be registered against the service collection
            // before BuildServiceProvider, so we cannot wait for the
            // factory-builder lambda below to surface the loaded
            // ConfigDocument.
            var bootstrapStore = new IviCli.Infrastructure.Configuration.TomlConfigStore(
                new System.IO.Abstractions.FileSystem(),
                configPath
            );
            var bootstrapLoad = bootstrapStore
                .LoadAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            var bootstrapConfig = bootstrapLoad
                is Result<
                    IviCli.Domain.Configuration.ConfigDocument,
                    IviCli.Application.Configuration.ConfigStoreError
                >.Ok { Value: var bootCfg }
                ? bootCfg
                : IviCli.Domain.Configuration.ConfigDocument.Empty;
            IviCli.Cli.Telemetry.TelemetryBootstrapper.Install(services, bootstrapConfig.Telemetry);
            services.AddIviCliApiTokenStore(
                Path.Combine(IviPaths.ResolveAuthDirectory(), "api-tokens.toml")
            );

            // Audit log (ADR 0043). Default-on; the path comes from the
            // [audit] override when supplied, otherwise IviPaths-derived.
            if (bootstrapConfig.Audit.Enabled)
            {
                var auditPath =
                    bootstrapConfig.Audit.Path
                    ?? Path.Combine(IviPaths.ResolveAuditDirectory(), "audit.ndjson");
                services.AddIviCliAuditLog(auditPath);
            }
            else
            {
                services.AddSingleton<IviCli.Application.Audit.IAuditLog>(
                    IviCli.Application.Audit.NullAuditLog.Instance
                );
            }

            // Audit subject (ADR 0043 Batch U). CliAuditSubject returns
            // 'cli/{Environment.UserName}' so ConfigMutated and
            // ServerLifecycle events attribute the actor.
            services.AddSingleton<
                IviCli.Application.Audit.IAuditSubject,
                IviCli.Cli.Audit.CliAuditSubject
            >();

            // Plugin discovery (ADR 0013) — opt-in. When enabled, each
            // loaded plugin's Register call adds its IIviBackend types as
            // DI singletons; the PluginBackendFactory decorator (below)
            // dispatches to them by VisaResource matcher.
            var pluginRegistrations =
                new List<IviCli.Infrastructure.Plugins.PluginBackendRegistration>();
            if (
                bootstrapConfig.Plugins.Enabled
                && !IviCli.Infrastructure.Plugins.PluginSupport.IsSupported
            )
            {
                Log.Logger.Warning(
                    "[plugins] enabled = true, but this build was published without plugin support (trimmed/AOT); skipping plugin discovery"
                );
            }
            else if (
                IviCli.Infrastructure.Plugins.PluginSupport.IsSupported
                && bootstrapConfig.Plugins.Enabled
            )
            {
                var pluginLoader = new IviCli.Infrastructure.Plugins.PluginLoader(
                    new System.IO.Abstractions.FileSystem()
                );
                var loaded = pluginLoader.LoadAll(
                    bootstrapConfig.Plugins,
                    IviPaths.ResolvePluginsDirectory()
                );
                var pluginServices = new IviCli.Infrastructure.Plugins.PluginServices(services);
                foreach (var plugin in loaded)
                {
                    try
                    {
                        plugin.Instance.Register(pluginServices);
                        Log.Logger.Information(
                            "loaded plugin {Name} v{Version}",
                            plugin.Manifest.Name,
                            plugin.Manifest.Version
                        );
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Warning(
                            ex,
                            "plugin {Name} Register() threw; skipping",
                            plugin.Manifest.Name
                        );
                    }
                }
                pluginRegistrations.AddRange(pluginServices.Registrations);
            }
            services.AddSingleton<IviCli.Api.Authentication.ApiAuthenticationOptions>();
            services.AddIviCliScenarioStore(configPath);
            services.AddIviCliBackendsFake();
            services.AddIviCliBackendsSocket();
            services.AddIviCliBackendsHiSlip();
            services.AddIviCliBackendsVxi11();
            services.AddIviCliBackendsLocal();

            // Discovery scanners (ADR 0008, Batch W). Registered as
            // additional IBackendScanner implementations alongside the
            // FakeBackendScanner so `ivicli visa scan` lights up
            // without per-batch CLI changes. Safe to register in the
            // mock-only container too — the LAN probes simply return
            // nothing when the container is alone on its network.
            services.AddIviCliLxiScanner();
            services.AddIviCliVxi11Scanner();
            services.AddIviCliSocketScanner();
            services.AddIviCliLocalUsbScanner();
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
                // IVICLI_MOCK_ONLY=1 collapses every transport-specific
                // backend to the fallback (FakeBackend), so every gateway
                // device resolves to the in-process mock — required for
                // the scenario-driven mock-VISA container (ADR 0018).
                // Combined with IVICLI_SCENARIO=<name> the gateway speaks
                // SCPI from the activated scenario without any outbound
                // connection attempt.
                var mockOnly =
                    Environment.GetEnvironmentVariable("IVICLI_MOCK_ONLY") == "1"
                    || string.Equals(
                        Environment.GetEnvironmentVariable("IVICLI_MOCK_ONLY"),
                        "true",
                        StringComparison.OrdinalIgnoreCase
                    );
                var hislipBackend = mockOnly
                    ? null
                    : sp.GetRequiredService<IviCli.Backends.HiSlip.HiSlipBackend>();
                if (hislipBackend is not null)
                {
                    // [telemetry] hislip_propagation: precede each op with the
                    // caller's W3C trace context so an ivi-cli gateway joins
                    // the trace (ADR 0040). Off by default — foreign HiSLIP
                    // servers answer the vendor message with a non-fatal Error.
                    hislipBackend.PropagateTraceContext =
                        bootstrapConfig.Telemetry.Enabled
                        && bootstrapConfig.Telemetry.HiSlipPropagationEnabled;
                }
                IviCli.Application.Backends.IBackendFactory factory =
                    new IviCli.Infrastructure.Backends.DefaultBackendFactory(
                        fallbackBackend: fallback,
                        localBackend: mockOnly
                            ? null
                            : sp.GetRequiredService<IviCli.Backends.Local.LocalBackend>(),
                        hislipBackend: hislipBackend,
                        socketBackend: mockOnly
                            ? null
                            : sp.GetRequiredService<IviCli.Backends.Socket.SocketBackend>(),
                        vxi11Backend: mockOnly
                            ? null
                            : sp.GetRequiredService<IviCli.Backends.Vxi11.Vxi11Backend>()
                    );

                // Instrumenting layer always wraps Default — Activity /
                // Meter calls are near-free without listeners, so this
                // is cheap even when [telemetry] is disabled (ADR 0040).
                factory = new IviCli.Application.Backends.InstrumentingBackendFactory(factory);

                // Plugin layer consults plugin-registered backends before
                // delegating to the built-in routing (ADR 0013). Only
                // active when [plugins].enabled was true at startup.
                if (pluginRegistrations.Count > 0)
                {
                    factory = new IviCli.Infrastructure.Plugins.PluginBackendFactory(
                        factory,
                        sp,
                        pluginRegistrations
                    );
                }

                // Pool layer wraps the default factory when [pool] enabled.
                // Capture wraps Pool so logical Open/Close events still
                // appear 1:1 in the audit trail even when the pool elides
                // the underlying wire opens (ADR 0038 §5).
                var loadedConfig =
                    sp.GetRequiredService<IviCli.Application.Configuration.IConfigStore>()
                        .LoadAsync(CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                if (
                    loadedConfig
                        is Result<
                            IviCli.Domain.Configuration.ConfigDocument,
                            IviCli.Application.Configuration.ConfigStoreError
                        >.Ok { Value: var cfg }
                    && cfg.Pool.Enabled
                )
                {
                    var poolLogger = sp.GetService<
                        ILogger<IviCli.Application.Backends.PoolingBackendFactory>
                    >();
                    factory = new IviCli.Application.Backends.PoolingBackendFactory(
                        factory,
                        cfg.Pool,
                        sp.GetRequiredService<TimeProvider>(),
                        poolLogger
                    );
                }

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

            var root = BuildRoot(provider);
            var parseResult = root.Parse(args);

            // Activate any scenario named by IVICLI_SCENARIO (env wins) or
            // session.json (persisted) before the command runs, so visa
            // subcommands see the scenario on this same invocation. Parsing
            // first costs nothing and tells us when the invocation prints
            // help or a version and touches no backend at all.
            if (ScenarioActivation.IsNeededFor(parseResult))
            {
                await ActivateScenarioIfRequested(provider, cts.Token);
            }

            return await parseResult.InvokeAsync(cancellationToken: cts.Token);
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

        var root = new RootCommand(
            "ivi-cli: integrated CLI for managing, diagnosing, and operating VISA/IVI instruments."
        );
        root.Subcommands.Add(visa);
        root.Subcommands.Add(MockCommand.Build(services));
        root.Subcommands.Add(ServerCommand.Build(services));
        root.Subcommands.Add(ApiCommand.Build(services));
        root.Subcommands.Add(DoctorCommand.Build(services));
        root.Subcommands.Add(DriverCommand.Build(services));
        root.Subcommands.Add(LogicalCommand.Build(services));
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

        // mock scene / rule add|remove: the owning scenario is their first positional
        foreach (var noun in new[] { "scene", "rule" })
        {
            var nounCmd = mock.Subcommands.FirstOrDefault(c => c.Name == noun);
            foreach (var sub in nounCmd?.Subcommands ?? [])
            {
                if (sub.Arguments.Count > 0)
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
        var store = services.GetRequiredService<IScenarioStore>();
        var fake = services.GetRequiredService<FakeBackend>();

        // Highest precedence: IVICLI_SCENARIO env var. The target device
        // is resolved in this order: IVICLI_SCENARIO_FOR (explicit env)
        // → session.CurrentDevice (the `visa use` default). When neither
        // is set, the env var is ignored with a warning — there's nothing
        // to bind to. The explicit env exists so containers and CI runs
        // can pre-load a scenario without writing session.json first
        // (Dockerfile `IVICLI_SCENARIO_FOR=mock1` covers the canonical
        // mock-container case).
        string? envScenario = Environment.GetEnvironmentVariable("IVICLI_SCENARIO");
        string? envScenarioFor = Environment.GetEnvironmentVariable("IVICLI_SCENARIO_FOR");

        var sessionStore = services.GetRequiredService<ISessionStore>();
        var sessionResult = await sessionStore.LoadAsync(ct);
        if (sessionResult is not Result<SessionState, SessionStoreError>.Ok { Value: var session })
        {
            return;
        }

        if (!string.IsNullOrEmpty(envScenario))
        {
            DeviceName? envTarget = null;
            if (!string.IsNullOrEmpty(envScenarioFor))
            {
                if (
                    DeviceName.From(envScenarioFor) is Result<DeviceName, DeviceError>.Ok
                    {
                        Value: var parsed
                    }
                )
                {
                    envTarget = parsed;
                }
                else
                {
                    Log.Logger.Warning(
                        "ignoring invalid IVICLI_SCENARIO_FOR value {Raw}",
                        envScenarioFor
                    );
                }
            }
            envTarget ??= session.CurrentDevice;

            if (envTarget is null)
            {
                Log.Logger.Warning(
                    "IVICLI_SCENARIO={Name} ignored: no target device (set IVICLI_SCENARIO_FOR=<device> or run `ivicli visa use <device>` first)",
                    envScenario
                );
            }
            else if (
                ScenarioName.From(envScenario)
                is not Result<ScenarioName, ScenarioNameError>.Ok { Value: var envName }
            )
            {
                Log.Logger.Warning("ignoring invalid IVICLI_SCENARIO value {Raw}", envScenario);
            }
            else
            {
                await ActivateOne(store, fake, envTarget, envName, ct);
                // Persist the env-var activation into the session. The live
                // scenario-binding refresher treats the session as the source
                // of truth and deactivates any running binding the session no
                // longer names; without this write it would tear down an
                // env-activated scenario (e.g. the mock container's
                // IVICLI_SCENARIO) on the first request. Mirrors what
                // `mock scenario activate` records.
                var bound = session.BindScenario(envTarget, envName);
                var saved = await sessionStore.SaveAsync(bound, ct);
                if (saved is Result<Unit, SessionStoreError>.Error { Err: var saveError })
                {
                    Log.Logger.Write(
                        Logging.SerilogConfiguration.ToLogEventLevel(saveError.Severity),
                        saveError.Cause,
                        saveError.Message,
                        saveError.LogArgs.ToArray()
                    );
                    Log.Logger.Warning(
                        "scenario binding {Scenario} -> {Device} was not persisted; a gateway session refresh may deactivate it",
                        envName.Value,
                        envTarget.Value
                    );
                }
            }
        }

        foreach (var (device, scenarioName) in session.DeviceScenarios)
        {
            await ActivateOne(store, fake, device, scenarioName, ct);
        }
    }

    private static async Task ActivateOne(
        IScenarioStore store,
        FakeBackend fake,
        DeviceName device,
        ScenarioName name,
        CancellationToken ct
    )
    {
        var loadResult = await store.LoadAsync(name, ct);
        if (loadResult is not Result<MockScenario, ScenarioStoreError>.Ok { Value: var scenario })
        {
            Log.Logger.Warning(
                "could not load active scenario {Name} for device {Device}: ignored",
                name.Value,
                device.Value
            );
            return;
        }
        fake.ActivateScenario(scenario, device);
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
