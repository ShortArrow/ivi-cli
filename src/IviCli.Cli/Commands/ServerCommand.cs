using System.CommandLine;
using IviCli.Application.Logging;
using IviCli.Application.Servers;
using IviCli.Domain;
using IviCli.Domain.Protocols;
using IviCli.Domain.Servers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>
/// Builds the <c>ivicli server</c> command tree. v1 covers server CRUD,
/// route CRUD, and the lifecycle commands (start / stop / status).
/// </summary>
public static class ServerCommand
{
    /// <summary>Returns the <c>server</c> command.</summary>
    public static Command Build(IServiceProvider services)
    {
        var command = new Command(
            "server",
            "Manage gateway servers, routes, and the lifecycle of running listeners."
        );
        command.Subcommands.Add(BuildList(services));
        command.Subcommands.Add(BuildAdd(services));
        command.Subcommands.Add(BuildRemove(services));
        command.Subcommands.Add(ServerRouteCommand.Build(services));
        var (start, stop, status) = ServerLifecycleCommand.BuildAll(services);
        command.Subcommands.Add(start);
        command.Subcommands.Add(stop);
        command.Subcommands.Add(status);
        command.Subcommands.Add(ServerLogCommand.Build());
        return command;
    }

    private static Command BuildList(IServiceProvider services)
    {
        var cmd = new Command("list", "List configured gateway servers.");
        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var handler = services.GetRequiredService<ListServersQueryHandler>();
                var logger = services.GetRequiredService<ILogger<ListServersQueryHandler>>();
                var result = await handler.HandleAsync(new ListServersQuery(), ct);
                return result switch
                {
                    Result<ServerListing, ListServersError>.Ok ok => Render(ok.Value),
                    Result<ServerListing, ListServersError>.Error err => Log(
                        err.Err,
                        logger,
                        "error: failed to list servers.",
                        ExitCodeMapper.ConfigurationError
                    ),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );
        return cmd;
    }

    private static int Render(ServerListing listing)
    {
        if (listing.Servers.IsEmpty)
        {
            Console.WriteLine("(no servers configured)");
        }
        else
        {
            foreach (var s in listing.Servers)
            {
                Console.WriteLine(
                    $"{s.Name.Value}\t{s.Type.ToString().ToLowerInvariant()}\t{s.Bind.Value}:{s.Port.Value}"
                );
            }
        }
        return ExitCodeMapper.Success;
    }

    private static Command BuildAdd(IServiceProvider services)
    {
        var nameArg = new Argument<string>("name") { Description = "Server alias." };
        var typeOpt = new Option<string>("--type")
        {
            Description = "Server type: local, socket, hislip, vxi11, usbip.",
            Required = true,
        };
        var bindOpt = new Option<string>("--bind")
        {
            Description = "Bind address (default 127.0.0.1).",
            DefaultValueFactory = _ => "127.0.0.1",
        };
        var portOpt = new Option<int>("--port")
        {
            Description = "TCP port to listen on (defaults: socket=5025, hislip=4880, usbip=3240).",
            DefaultValueFactory = _ => 0,
        };

        var cmd = new Command("add", "Register a new gateway server.");
        cmd.Arguments.Add(nameArg);
        cmd.Options.Add(typeOpt);
        cmd.Options.Add(bindOpt);
        cmd.Options.Add(portOpt);

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var name = parseResult.GetRequiredValue(nameArg);
                var type = parseResult.GetRequiredValue(typeOpt);
                var bind = parseResult.GetValue(bindOpt) ?? "127.0.0.1";
                var port = parseResult.GetValue(portOpt);
                if (port == 0)
                {
                    port = type.ToLowerInvariant() switch
                    {
                        "socket" => 5025,
                        "hislip" => 4880,
                        "usbip" => UsbIpConstants.DefaultPort,
                        _ => 0,
                    };
                }

                var handler = services.GetRequiredService<AddServerCommandHandler>();
                var logger = services.GetRequiredService<ILogger<AddServerCommandHandler>>();
                var result = await handler.HandleAsync(
                    new AddServerCommand(name, type, bind, port),
                    ct
                );
                return result switch
                {
                    Result<ServerName, AddServerError>.Ok ok => Ok(
                        $"added server {ok.Value.Value}"
                    ),
                    Result<ServerName, AddServerError>.Error err => err.Err switch
                    {
                        AddServerInvalidName n => Log(
                            err.Err,
                            logger,
                            $"error: invalid server name '{n.Raw}'.",
                            ExitCodeMapper.UsageError
                        ),
                        AddServerInvalidType t => Log(
                            err.Err,
                            logger,
                            $"error: unknown server type '{t.Raw}'.",
                            ExitCodeMapper.UsageError
                        ),
                        AddServerInvalidBind b => Log(
                            err.Err,
                            logger,
                            $"error: invalid bind '{b.Raw}'.",
                            ExitCodeMapper.UsageError
                        ),
                        AddServerInvalidPort p => Log(
                            err.Err,
                            logger,
                            $"error: invalid port {p.Raw}.",
                            ExitCodeMapper.UsageError
                        ),
                        AddServerDuplicate d => Log(
                            err.Err,
                            logger,
                            $"error: server '{d.Name.Value}' already exists.",
                            ExitCodeMapper.DeviceError
                        ),
                        _ => Log(
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

    private static Command BuildRemove(IServiceProvider services)
    {
        var nameArg = new Argument<string>("name") { Description = "Server alias to remove." };

        var cmd = new Command("remove", "Remove a registered gateway server (cascades routes).");
        cmd.Arguments.Add(nameArg);

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var name = parseResult.GetRequiredValue(nameArg);
                var handler = services.GetRequiredService<RemoveServerCommandHandler>();
                var logger = services.GetRequiredService<ILogger<RemoveServerCommandHandler>>();
                var result = await handler.HandleAsync(new RemoveServerCommand(name), ct);
                return result switch
                {
                    Result<ServerName, RemoveServerError>.Ok ok => Ok(
                        $"removed server {ok.Value.Value}"
                    ),
                    Result<ServerName, RemoveServerError>.Error err => err.Err switch
                    {
                        RemoveServerInvalidName n => Log(
                            err.Err,
                            logger,
                            $"error: invalid server name '{n.Raw}'.",
                            ExitCodeMapper.UsageError
                        ),
                        RemoveServerNotFound nf => Log(
                            err.Err,
                            logger,
                            $"error: no such server '{nf.Name.Value}'.",
                            ExitCodeMapper.DeviceError
                        ),
                        _ => Log(
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

    internal static int Ok(string message)
    {
        Console.WriteLine(message);
        return ExitCodeMapper.Success;
    }

    internal static int Log(IviError error, ILogger logger, string userMessage, int exitCode)
    {
        logger.LogIviError(error);
        Console.Error.WriteLine(userMessage);
        return exitCode;
    }
}
