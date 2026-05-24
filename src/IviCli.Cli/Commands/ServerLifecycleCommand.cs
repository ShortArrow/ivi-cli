using System.CommandLine;
using System.Diagnostics;
using IviCli.Application.Servers;
using IviCli.Domain;
using IviCli.Domain.Servers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>
/// Builds the <c>server start / stop / status</c> commands. v1 implements
/// <c>start</c> as a foreground process (Ctrl+C / cancellation stops it);
/// <c>stop</c> is therefore not a remote command but a hint; <c>status</c>
/// reports the configured servers from <c>config.toml</c>.
/// </summary>
public static class ServerLifecycleCommand
{
    /// <summary>
    /// Returns the start / stop / status commands as a tuple for the parent
    /// to attach directly under <c>server</c>.
    /// </summary>
    public static (Command Start, Command Stop, Command Status) BuildAll(
        IServiceProvider services
    ) => (BuildStart(services), BuildStop(services), BuildStatus(services));

    /// <summary>Backwards-compatible placeholder so other callers of Build do not break.</summary>
    public static Command Build(IServiceProvider services)
    {
        var container = new Command("lifecycle", "(internal) gateway lifecycle commands.");
        var (start, stop, status) = BuildAll(services);
        container.Subcommands.Add(start);
        container.Subcommands.Add(stop);
        container.Subcommands.Add(status);
        container.Hidden = true;
        return container;
    }

    private static Command BuildStart(IServiceProvider services)
    {
        var nameArg = new Argument<string>("name")
        {
            Description = "Configured server name to start (see `server list`).",
        };

        var cmd = new Command(
            "start",
            "Start a configured gateway server in the foreground (Ctrl+C to stop)."
        );
        cmd.Arguments.Add(nameArg);

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var name = parseResult.GetRequiredValue(nameArg);
                var handler = services.GetRequiredService<StartServerCommandHandler>();
                var logger = services.GetRequiredService<ILogger<StartServerCommandHandler>>();

                var result = await handler.HandleAsync(new StartServerCommand(name), ct);
                return result switch
                {
                    Result<Unit, StartServerError>.Ok => ExitCodeMapper.Success,
                    Result<Unit, StartServerError>.Error err => err.Err switch
                    {
                        StartServerInvalidName n => ServerCommand.Log(
                            err.Err,
                            logger,
                            $"error: invalid server name '{n.Raw}'.",
                            ExitCodeMapper.UsageError
                        ),
                        StartServerUnknown u => ServerCommand.Log(
                            err.Err,
                            logger,
                            $"error: no such server '{u.Name.Value}'.",
                            ExitCodeMapper.DeviceError
                        ),
                        StartServerLifecycleFailure => ServerCommand.Log(
                            err.Err,
                            logger,
                            "error: gateway server failed.",
                            ExitCodeMapper.TransportError
                        ),
                        _ => ServerCommand.Log(
                            err.Err,
                            logger,
                            "error: configuration storage failed.",
                            ExitCodeMapper.ConfigurationError
                        ),
                    },
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );
        return cmd;
    }

    private static Command BuildStop(IServiceProvider services)
    {
        var nameArg = new Argument<string>("name")
        {
            Description = "Configured server name whose process should be terminated.",
        };
        var cmd = new Command(
            "stop",
            "Send a termination signal to the process recorded in the server's PID file."
        );
        cmd.Arguments.Add(nameArg);

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var name = parseResult.GetRequiredValue(nameArg);
                var handler = services.GetRequiredService<StopServerCommandHandler>();
                var logger = services.GetRequiredService<ILogger<StopServerCommandHandler>>();

                var result = await handler.HandleAsync(new StopServerCommand(name), ct);
                if (result is not Result<ServerProcessEntry, StopServerError>.Ok ok)
                {
                    var err = ((Result<ServerProcessEntry, StopServerError>.Error)result).Err;
                    return err switch
                    {
                        StopServerInvalidName n => ServerCommand.Log(
                            err,
                            logger,
                            $"error: invalid server name '{n.Raw}'.",
                            ExitCodeMapper.UsageError
                        ),
                        StopServerUnknown u => ServerCommand.Log(
                            err,
                            logger,
                            $"error: no such server '{u.Name.Value}'.",
                            ExitCodeMapper.DeviceError
                        ),
                        StopServerNotRunning nr => ServerCommand.Log(
                            err,
                            logger,
                            $"error: server '{nr.Name.Value}' is not running.",
                            ExitCodeMapper.UsageError
                        ),
                        StopServerRegistryFailure => ServerCommand.Log(
                            err,
                            logger,
                            "error: server process registry failure.",
                            ExitCodeMapper.ConfigurationError
                        ),
                        _ => ServerCommand.Log(
                            err,
                            logger,
                            "error: configuration storage failed.",
                            ExitCodeMapper.ConfigurationError
                        ),
                    };
                }

                var entry = ok.Value;
                var killed = TryKillProcess(entry.ProcessId);
                if (!killed)
                {
                    // Process already gone; remove the stale PID file anyway.
                    _ = await handler.ClearEntryAsync(entry.Name, ct);
                    Console.Error.WriteLine(
                        $"server '{entry.Name.Value}' (pid {entry.ProcessId}) was not running; cleared stale PID file."
                    );
                    return ExitCodeMapper.Success;
                }
                _ = await handler.ClearEntryAsync(entry.Name, ct);
                Console.WriteLine($"stopped server '{entry.Name.Value}' (pid {entry.ProcessId}).");
                return ExitCodeMapper.Success;
            }
        );
        return cmd;
    }

    private static bool TryKillProcess(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            proc.Kill(entireProcessTree: false);
            // Wait a brief moment for the OS to reap the process so a
            // subsequent `server status` does not still report running.
            proc.WaitForExit(milliseconds: 5_000);
            return true;
        }
        catch (ArgumentException)
        {
            // No such process.
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static Command BuildStatus(IServiceProvider services)
    {
        var cmd = new Command("status", "Report configured gateway servers and routes.");
        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var listServers = services.GetRequiredService<ListServersQueryHandler>();
                var listRoutes = services.GetRequiredService<ListRoutesQueryHandler>();
                var registry = services.GetRequiredService<IServerProcessRegistry>();

                var servers = await listServers.HandleAsync(new ListServersQuery(), ct);
                var routes = await listRoutes.HandleAsync(new ListRoutesQuery(), ct);
                if (servers is not Result<ServerListing, ListServersError>.Ok serversOk)
                {
                    Console.Error.WriteLine("error: failed to read configuration.");
                    return ExitCodeMapper.ConfigurationError;
                }
                if (routes is not Result<RouteListing, ListRoutesError>.Ok routesOk)
                {
                    Console.Error.WriteLine("error: failed to read configuration.");
                    return ExitCodeMapper.ConfigurationError;
                }

                Console.WriteLine("servers:");
                if (serversOk.Value.Servers.IsEmpty)
                {
                    Console.WriteLine("  (none)");
                }
                else
                {
                    foreach (var s in serversOk.Value.Servers)
                    {
                        var runningStatus = await DescribeRunningStateAsync(registry, s.Name, ct);
                        Console.WriteLine(
                            $"  {s.Name.Value} ({s.Type.ToString().ToLowerInvariant()}) "
                                + $"{s.Bind.Value}:{s.Port.Value}  {runningStatus}"
                        );
                    }
                }
                Console.WriteLine("routes:");
                if (routesOk.Value.Routes.IsEmpty)
                {
                    Console.WriteLine("  (none)");
                }
                else
                {
                    foreach (var r in routesOk.Value.Routes)
                    {
                        Console.WriteLine(
                            $"  {r.ServerName.Value}/{r.Endpoint.Value} -> {r.DeviceName.Value}"
                        );
                    }
                }
                return ExitCodeMapper.Success;
            }
        );
        return cmd;
    }

    private static async Task<string> DescribeRunningStateAsync(
        IServerProcessRegistry registry,
        ServerName name,
        CancellationToken ct
    )
    {
        var read = await registry.ReadAsync(name, ct);
        if (
            read
            is not Result<ServerProcessEntry?, ServerProcessRegistryError>.Ok { Value: var entry }
        )
        {
            return "[registry unreadable]";
        }
        if (entry is null)
        {
            return "[stopped]";
        }
        try
        {
            using var proc = Process.GetProcessById(entry.ProcessId);
            return $"[running pid {entry.ProcessId}]";
        }
        catch (ArgumentException)
        {
            return $"[stale pid {entry.ProcessId}]";
        }
    }
}
