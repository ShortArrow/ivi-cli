using System.CommandLine;
using System.Globalization;
using IviCli.Application.Mock;
using IviCli.Application.Servers;
using IviCli.Domain;
using IviCli.Domain.Protocols;
using IviCli.Domain.Servers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>
/// Builds <c>mock serve</c>: registers what serves a scenario at one
/// address, says how a client connects, and runs the server in the
/// foreground as <c>server start</c> does (ADR 0056).
/// </summary>
public static class MockServeCommand
{
    /// <summary>Returns the <c>serve</c> command.</summary>
    public static Command Build(IServiceProvider services)
    {
        var scenarioArg = new Argument<string>("scenario")
        {
            Description = "The scenario to serve (see `mock scenario list`).",
        };
        var typeOpt = new Option<string>("--type")
        {
            Description = "How a client connects: socket, hislip, vxi11 or usbip.",
            Required = true,
        };
        var portOpt = new Option<int?>("--port")
        {
            Description =
                "TCP port to listen on (defaults: socket=5025, hislip=4880, vxi11=1024, usbip=3240).",
        };
        var aliasOpt = new Option<string?>("--alias")
        {
            Description =
                "Name of the mock's device and server (default mock-<type>-<port>).",
        };
        var bindOpt = new Option<string>("--bind")
        {
            Description = "Bind address (default 127.0.0.1).",
            DefaultValueFactory = _ => "127.0.0.1",
        };
        var profileOpt = new Option<string?>("--profile")
        {
            Description = "USB profile for --type usbip: usbtmc (default) or cdc-acm.",
        };

        var cmd = new Command(
            "serve",
            "Serve a scenario at one address, registering its device, server and route, "
                + "and run the server in the foreground (Ctrl+C to stop)."
        );
        cmd.Arguments.Add(scenarioArg);
        cmd.Options.Add(typeOpt);
        cmd.Options.Add(portOpt);
        cmd.Options.Add(aliasOpt);
        cmd.Options.Add(bindOpt);
        cmd.Options.Add(profileOpt);

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var command = new ServeMockCommand(
                    parseResult.GetRequiredValue(scenarioArg),
                    parseResult.GetRequiredValue(typeOpt),
                    parseResult.GetValue(portOpt),
                    parseResult.GetValue(aliasOpt),
                    parseResult.GetValue(bindOpt) ?? "127.0.0.1",
                    parseResult.GetValue(profileOpt)
                );
                var handler = services.GetRequiredService<ServeMockCommandHandler>();
                var logger = services.GetRequiredService<ILogger<ServeMockCommandHandler>>();

                var result = await handler.HandleAsync(command, ct);
                if (result is Result<ServedMock, ServeMockError>.Error err)
                {
                    return ServerCommand.Log(
                        err.Err,
                        logger,
                        UserFacingMessage(err.Err),
                        ExitCode(err.Err)
                    );
                }
                var served = ((Result<ServedMock, ServeMockError>.Ok)result).Value;
                Console.WriteLine(
                    $"{served.Device.Name.Value} serves the scenario {served.Scenario.Value}."
                );
                Console.WriteLine(HowToConnect(served.Server));

                var start = services.GetRequiredService<StartServerCommandHandler>();
                var startLogger = services.GetRequiredService<ILogger<StartServerCommandHandler>>();
                var run = await start.HandleAsync(
                    new StartServerCommand(served.Server.Name.Value),
                    ct
                );
                return ServerLifecycleCommand.ReportStart(run, startLogger);
            }
        );
        return cmd;
    }

    /// <summary>
    /// The line that tells a client how to reach a server <c>mock serve</c>
    /// made: the VISA resource, or for <c>usbip</c> the attach command. A
    /// server bound to every address is reached at <c>127.0.0.1</c>.
    /// </summary>
    public static string HowToConnect(Domain.Servers.Server server)
    {
        var host = server.Bind.Value is "0.0.0.0" or "::" ? "127.0.0.1" : server.Bind.Value;
        var port = server.Port.Value.ToString(CultureInfo.InvariantCulture);
        return server.Type switch
        {
            ServerType.Socket => $"connect: TCPIP::{host}::{port}::SOCKET",
            ServerType.HiSlip => $"connect: TCPIP::{host}::hislip0,{port}::INSTR",
            ServerType.Vxi11 => $"connect: TCPIP::{host}::inst0,{port}::INSTR",
            _ => server.Port.Value == UsbIpConstants.DefaultPort
                ? $"attach: usbip attach -r {host} -b 1-1"
                : $"attach: usbip --tcp-port {port} attach -r {host} -b 1-1",
        };
    }

    private static string UserFacingMessage(ServeMockError error) =>
        error switch
        {
            ServeMockInvalidScenario s => $"error: invalid scenario name '{s.Raw}'.",
            ServeMockScenarioNotFound n =>
                $"error: no scenario '{n.Name.Value}'; create it with `mock scenario create`.",
            ServeMockInvalidType t =>
                $"error: invalid type '{t.Raw}'; use socket, hislip, vxi11 or usbip.",
            ServeMockInvalidPort p => $"error: invalid port {p.Raw}.",
            ServeMockInvalidBind b => $"error: invalid bind '{b.Raw}'.",
            ServeMockInvalidProfile p =>
                $"error: invalid profile '{p.Raw}'; use usbtmc or cdc-acm.",
            ServeMockInvalidAlias a => DeviceNameMessage.Invalid(a.Raw),
            ServeMockReservedName r =>
                $"error: '{r.Name.Value}' is reserved for the management API; choose another --alias.",
            ServeMockDeviceConflict d =>
                $"error: device '{d.Name.Value}' cannot serve the mock: {d.Difference}. Choose another --alias.",
            ServeMockServerConflict s =>
                $"error: server '{s.Name.Value}' cannot serve the mock: {s.Difference}. Choose another --alias.",
            ServeMockServerRunning r =>
                $"error: server '{r.Name.Value}' is already running (pid {r.ProcessId}).",
            ServeMockRegistryFailure => "error: server process registry failure.",
            ServeMockSessionFailure =>
                "error: session storage failed; the device, server and route are registered, "
                    + "and running `mock serve` again binds the scenario.",
            _ => "error: configuration storage failed.",
        };

    private static int ExitCode(ServeMockError error) =>
        error switch
        {
            ServeMockInvalidScenario
            or ServeMockInvalidType
            or ServeMockInvalidPort
            or ServeMockInvalidBind
            or ServeMockInvalidProfile
            or ServeMockInvalidAlias
            or ServeMockReservedName
            or ServeMockServerRunning => ExitCodeMapper.UsageError,
            ServeMockScenarioNotFound or ServeMockDeviceConflict or ServeMockServerConflict =>
                ExitCodeMapper.DeviceError,
            _ => ExitCodeMapper.ConfigurationError,
        };
}
