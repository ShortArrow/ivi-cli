using System.CommandLine;
using IviCli.Application.Servers;
using IviCli.Domain;
using IviCli.Domain.Servers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>Wires the <c>server route</c> subcommand tree (PRD §6.3).</summary>
public static class ServerRouteCommand
{
    /// <summary>Returns the <c>route</c> command, ready to attach under <c>server</c>.</summary>
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("route", "Manage server-to-device routes.");
        command.Subcommands.Add(BuildList(services));
        command.Subcommands.Add(BuildAdd(services));
        command.Subcommands.Add(BuildRemove(services));
        return command;
    }

    private static Command BuildList(IServiceProvider services)
    {
        var cmd = new Command("list", "List configured routes.");
        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var handler = services.GetRequiredService<ListRoutesQueryHandler>();
                var logger = services.GetRequiredService<ILogger<ListRoutesQueryHandler>>();
                var result = await handler.HandleAsync(new ListRoutesQuery(), ct);
                return result switch
                {
                    Result<RouteListing, ListRoutesError>.Ok ok => Render(ok.Value, Console.Out),
                    Result<RouteListing, ListRoutesError>.Error err => ServerCommand.Log(
                        err.Err,
                        logger,
                        "error: failed to list routes.",
                        ExitCodeMapper.ConfigurationError
                    ),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );
        return cmd;
    }

    /// <summary>Renders the route listing as plain text for human consumption.</summary>
    public static int Render(RouteListing listing, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(writer);

        if (listing.Routes.IsEmpty)
        {
            writer.WriteLine("(no routes configured)");
        }
        else
        {
            foreach (var r in listing.Routes)
            {
                writer.WriteLine(
                    $"[{r.Endpoint.Value}] {r.ServerName.Value} -> {r.DeviceName.Value}{ProfileMarker(r)}"
                );
            }
        }
        return ExitCodeMapper.Success;
    }

    /// <summary>
    /// The profile suffix, empty for the default. Shown only when it is a
    /// choice, the way the configuration file writes it only then: a
    /// route that says nothing about its profile reads the same as it did
    /// before routes had one.
    /// </summary>
    private static string ProfileMarker(Route route) =>
        route.Profile switch
        {
            UsbExportProfile.CdcAcm => " (cdc-acm)",
            _ => string.Empty,
        };

    private static Command BuildAdd(IServiceProvider services)
    {
        var serverArg = new Argument<string>("server") { Description = "Server alias." };
        var endpointArg = new Argument<string>("endpoint")
        {
            Description = "Public endpoint (hislip0 / 5025 etc.).",
        };
        var deviceArg = new Argument<string>("device")
        {
            Description = "Device alias to bind to this endpoint.",
        };

        var profileOpt = new Option<string>("--profile")
        {
            Description =
                "USB profile a usbip export presents: usbtmc (default; a VISA "
                + "USB::…::INSTR resource) or cdc-acm (a COM port through the "
                + "inbox serial driver). Ignored by every other server type.",
            DefaultValueFactory = _ => "usbtmc",
        };

        var cmd = new Command("add", "Bind a public endpoint to a local device.");
        cmd.Arguments.Add(serverArg);
        cmd.Arguments.Add(endpointArg);
        cmd.Arguments.Add(deviceArg);
        cmd.Options.Add(profileOpt);

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var server = parseResult.GetRequiredValue(serverArg);
                var endpoint = parseResult.GetRequiredValue(endpointArg);
                var device = parseResult.GetRequiredValue(deviceArg);
                var profile = parseResult.GetValue(profileOpt);

                var handler = services.GetRequiredService<AddRouteCommandHandler>();
                var logger = services.GetRequiredService<ILogger<AddRouteCommandHandler>>();
                var result = await handler.HandleAsync(
                    new AddRouteCommand(server, endpoint, device, profile),
                    ct
                );
                return result switch
                {
                    Result<Route, AddRouteError>.Ok ok => ServerCommand.Ok(
                        $"added route {ok.Value.ServerName.Value}/{ok.Value.Endpoint.Value} -> {ok.Value.DeviceName.Value}"
                    ),
                    Result<Route, AddRouteError>.Error err => err.Err switch
                    {
                        AddRouteInvalidServer i => ServerCommand.Log(
                            err.Err,
                            logger,
                            $"error: invalid server name '{i.Raw}'.",
                            ExitCodeMapper.UsageError
                        ),
                        AddRouteInvalidEndpoint i => ServerCommand.Log(
                            err.Err,
                            logger,
                            $"error: invalid endpoint '{i.Raw}'.",
                            ExitCodeMapper.UsageError
                        ),
                        AddRouteInvalidDevice i => ServerCommand.Log(
                            err.Err,
                            logger,
                            DeviceNameMessage.Invalid(i.Raw),
                            ExitCodeMapper.UsageError
                        ),
                        AddRouteInvalidProfile i => ServerCommand.Log(
                            err.Err,
                            logger,
                            $"error: unknown USB export profile '{i.Raw}' (expected usbtmc or cdc-acm).",
                            ExitCodeMapper.UsageError
                        ),
                        AddRouteServerMissing m => ServerCommand.Log(
                            err.Err,
                            logger,
                            $"error: no such server '{m.Server.Value}'.",
                            ExitCodeMapper.DeviceError
                        ),
                        AddRouteDeviceMissing m => ServerCommand.Log(
                            err.Err,
                            logger,
                            $"error: no such device '{m.Device.Value}'.",
                            ExitCodeMapper.DeviceError
                        ),
                        AddRouteDuplicate d => ServerCommand.Log(
                            err.Err,
                            logger,
                            $"error: route '{d.Server.Value}/{d.Endpoint.Value}' already exists.",
                            ExitCodeMapper.DeviceError
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

    private static Command BuildRemove(IServiceProvider services)
    {
        var serverArg = new Argument<string>("server") { Description = "Server alias." };
        var endpointArg = new Argument<string>("endpoint") { Description = "Public endpoint." };

        var cmd = new Command("remove", "Remove a server-to-device route.");
        cmd.Arguments.Add(serverArg);
        cmd.Arguments.Add(endpointArg);

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var server = parseResult.GetRequiredValue(serverArg);
                var endpoint = parseResult.GetRequiredValue(endpointArg);

                var handler = services.GetRequiredService<RemoveRouteCommandHandler>();
                var logger = services.GetRequiredService<ILogger<RemoveRouteCommandHandler>>();
                var result = await handler.HandleAsync(
                    new RemoveRouteCommand(server, endpoint),
                    ct
                );
                return result switch
                {
                    Result<Unit, RemoveRouteError>.Ok => ServerCommand.Ok("route removed"),
                    Result<Unit, RemoveRouteError>.Error err => err.Err switch
                    {
                        RemoveRouteInvalidServer i => ServerCommand.Log(
                            err.Err,
                            logger,
                            $"error: invalid server name '{i.Raw}'.",
                            ExitCodeMapper.UsageError
                        ),
                        RemoveRouteInvalidEndpoint i => ServerCommand.Log(
                            err.Err,
                            logger,
                            $"error: invalid endpoint '{i.Raw}'.",
                            ExitCodeMapper.UsageError
                        ),
                        RemoveRouteNotFound nf => ServerCommand.Log(
                            err.Err,
                            logger,
                            $"error: no such route '{nf.Server.Value}/{nf.Endpoint.Value}'.",
                            ExitCodeMapper.DeviceError
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
}
