using System.CommandLine;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using IviCli.Api;
using IviCli.Application.Servers;
using IviCli.Domain;
using IviCli.Domain.Servers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>
/// Builds the <c>ivicli api ...</c> subcommand tree (ADR 0034). The
/// Management API lives behind two verbs:
/// <list type="bullet">
/// <item><c>api start [--port 8080] [--bind 127.0.0.1]</c> — foreground listener.</item>
/// <item><c>api stop</c> — sends a Ctrl+C / SIGTERM to the running listener.</item>
/// </list>
/// </summary>
public static class ApiCommand
{
    /// <summary>The reserved <see cref="ServerName"/> used to track the API's PID file.</summary>
    public static readonly ServerName ReservedName = ServerName.From("ivi-management-api")
        is Result<ServerName, ServerNameError>.Ok ok
        ? ok.Value
        : throw new InvalidOperationException("ivi-management-api must be a valid ServerName");

    /// <summary>Default port the listener binds to when <c>--port</c> is not supplied.</summary>
    public const int DefaultPort = 8080;

    /// <summary>Default bind address (loopback only — non-loopback warns at startup).</summary>
    public const string DefaultBind = "127.0.0.1";

    /// <summary>Builds the configured <see cref="Command"/>.</summary>
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("api", "Manage the local Management API (HTTP JSON).");
        command.Subcommands.Add(BuildStart(services));
        command.Subcommands.Add(BuildStop(services));
        return command;
    }

    private static Command BuildStart(IServiceProvider services)
    {
        var portOpt = new Option<int>("--port")
        {
            Description = $"TCP port to bind on (default {DefaultPort}).",
            DefaultValueFactory = _ => DefaultPort,
        };
        var bindOpt = new Option<string>("--bind")
        {
            Description = $"Listen address (default {DefaultBind}).",
            DefaultValueFactory = _ => DefaultBind,
        };

        var cmd = new Command("start", "Start the Management API listener in the foreground.");
        cmd.Options.Add(portOpt);
        cmd.Options.Add(bindOpt);

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var port = parseResult.GetValue(portOpt);
                var bindRaw = parseResult.GetValue(bindOpt) ?? DefaultBind;
                if (!IPAddress.TryParse(bindRaw, out var bindAddr))
                {
                    Console.Error.WriteLine($"error: invalid --bind value '{bindRaw}'.");
                    return ExitCodeMapper.UsageError;
                }
                if (!IPAddress.IsLoopback(bindAddr))
                {
                    Serilog.Log.Logger.Warning(
                        "Management API bound to {Bind}; authentication is not implemented in v1 (ADR 0034).",
                        bindAddr
                    );
                }

                var registry = services.GetRequiredService<IServerProcessRegistry>();
                var loggerFactory = services.GetRequiredService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger("IviCli.Cli.Commands.ApiCommand");
                var pid = Environment.ProcessId;
                _ = await registry.WriteAsync(ReservedName, pid, DateTimeOffset.UtcNow, ct);

                logger.LogInformation(
                    "Management API listening on http://{Bind}:{Port}",
                    bindAddr,
                    port
                );

                try
                {
                    var app = IviCliApiBuilder.Build(services, bindAddr, port);
                    await ((IHost)app).RunAsync(ct);
                    return ExitCodeMapper.Success;
                }
                catch (OperationCanceledException)
                {
                    return ExitCodeMapper.Success;
                }
                finally
                {
                    _ = await registry.DeleteAsync(ReservedName, CancellationToken.None);
                }
            }
        );
        return cmd;
    }

    private static Command BuildStop(IServiceProvider services)
    {
        var cmd = new Command("stop", "Stop the running Management API listener.");

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var registry = services.GetRequiredService<IServerProcessRegistry>();
                var entryResult = await registry.ReadAsync(ReservedName, ct);
                if (
                    entryResult
                    is not Result<ServerProcessEntry?, ServerProcessRegistryError>.Ok
                    {
                        Value: { } entry,
                    }
                )
                {
                    Console.Error.WriteLine("error: Management API is not running.");
                    return ExitCodeMapper.DeviceError;
                }
                try
                {
                    using var process = Process.GetProcessById(entry.ProcessId);
                    SignalGracefulExit(process);
                    if (!process.WaitForExit(5000))
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    _ = await registry.DeleteAsync(ReservedName, ct);
                    Console.WriteLine("Management API stopped.");
                    return ExitCodeMapper.Success;
                }
                catch (ArgumentException)
                {
                    // Process is already gone — clean up the stale PID file.
                    _ = await registry.DeleteAsync(ReservedName, ct);
                    Console.WriteLine(
                        "Management API was not running (cleaned up stale PID file)."
                    );
                    return ExitCodeMapper.Success;
                }
            }
        );
        return cmd;
    }

    private static void SignalGracefulExit(Process process)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                process.CloseMainWindow();
            }
            catch
            {
                // No console window — fall through to Kill.
            }
        }
        else
        {
            // POSIX: send SIGTERM via the process group. We don't have a
            // managed cross-platform signal API, so fall back to Kill which
            // sends SIGKILL on POSIX. The 5 s grace-window above gives the
            // process a chance to exit on its own first if a hosting shell
            // forwards Ctrl+C.
            try
            {
                process.Kill(entireProcessTree: false);
            }
            catch
            {
                // Best-effort.
            }
        }
    }
}
