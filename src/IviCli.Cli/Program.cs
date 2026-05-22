using System.CommandLine;
using IviCli.Application;
using IviCli.Backends.Fake;
using IviCli.Cli.Commands;
using IviCli.Cli.Logging;
using IviCli.Cli.Paths;
using IviCli.Infrastructure;
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
            services.AddIviCliInfrastructure(configPath);
            services.AddIviCliBackendsFake();
            services.AddIviCliBackendFactory();

            await using var provider = services.BuildServiceProvider();

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

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
        visa.Subcommands.Add(VisaListCommand.Build(services));

        var root = new RootCommand(
            "ivi-cli: integrated CLI for managing, diagnosing, and operating VISA/IVI instruments."
        );
        root.Subcommands.Add(visa);
        return root;
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
