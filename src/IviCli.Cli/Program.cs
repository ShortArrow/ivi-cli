using System.CommandLine;
using IviCli.Application;
using IviCli.Application.Mock;
using IviCli.Application.Session;
using IviCli.Backends.Fake;
using IviCli.Backends.HiSlip;
using IviCli.Backends.Local;
using IviCli.Backends.Socket;
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
            services.AddIviCliBackendsLocal();
            // Composition-root wiring of DefaultBackendFactory: route TCPIP
            // HiSLIP -> HiSlipBackend, SOCKET-style TCPIP -> SocketBackend,
            // other TCPIP / USB / GPIB -> LocalBackend, fallback -> FakeBackend.
            services.AddSingleton<IviCli.Application.Backends.IBackendFactory>(
                sp => new IviCli.Infrastructure.Backends.DefaultBackendFactory(
                    fallbackBackend: sp.GetRequiredService<IviCli.Backends.Fake.FakeBackend>(),
                    localBackend: sp.GetRequiredService<IviCli.Backends.Local.LocalBackend>(),
                    hislipBackend: sp.GetRequiredService<IviCli.Backends.HiSlip.HiSlipBackend>(),
                    socketBackend: sp.GetRequiredService<IviCli.Backends.Socket.SocketBackend>()
                )
            );
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

        var mock = new Command("mock", "Manage mock-device behaviour for the Fake Backend.");
        mock.Subcommands.Add(MockScenarioCommand.Build(services));

        var root = new RootCommand(
            "ivi-cli: integrated CLI for managing, diagnosing, and operating VISA/IVI instruments."
        );
        root.Subcommands.Add(visa);
        root.Subcommands.Add(mock);
        root.Subcommands.Add(ServerCommand.Build(services));
        root.Subcommands.Add(DiagnoseCommand.Build(services));
        return root;
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
