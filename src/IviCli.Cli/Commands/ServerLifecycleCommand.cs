using System.CommandLine;
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
    ) => (BuildStart(services), BuildStop(), BuildStatus(services));

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

    private static Command BuildStop()
    {
        var cmd = new Command(
            "stop",
            "Stop a running gateway. v1 runs in the foreground; send SIGINT / Ctrl+C to the running process."
        );
        cmd.SetAction(
            (parseResult, ct) =>
            {
                Console.Error.WriteLine(
                    "server stop: v1 runs in the foreground. Press Ctrl+C in the running `server start` window to stop it."
                );
                return Task.FromResult(ExitCodeMapper.UsageError);
            }
        );
        return cmd;
    }

    private static Command BuildStatus(IServiceProvider services)
    {
        var cmd = new Command("status", "Report configured gateway servers and routes.");
        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var listServers = services.GetRequiredService<ListServersQueryHandler>();
                var listRoutes = services.GetRequiredService<ListRoutesQueryHandler>();

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
                        Console.WriteLine(
                            $"  {s.Name.Value} ({s.Type.ToString().ToLowerInvariant()}) {s.Bind.Value}:{s.Port.Value}"
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
}
