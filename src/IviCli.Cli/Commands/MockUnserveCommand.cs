using System.CommandLine;
using IviCli.Application.Mock;
using IviCli.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>
/// Builds <c>mock unserve</c>: removes the device, binding, server and route
/// <c>mock serve</c> registered under a name, and keeps the scenario
/// (ADR 0056 §7).
/// </summary>
public static class MockUnserveCommand
{
    /// <summary>Returns the <c>unserve</c> command.</summary>
    public static Command Build(IServiceProvider services)
    {
        var nameArg = new Argument<string>("name")
        {
            Description = "The name `mock serve` printed, or the --alias given to it.",
        };
        var cmd = new Command(
            "unserve",
            "Remove the device, binding, server and route `mock serve` registered; the scenario is kept."
        );
        cmd.Arguments.Add(nameArg);

        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var handler = services.GetRequiredService<UnserveMockCommandHandler>();
                var logger = services.GetRequiredService<ILogger<UnserveMockCommandHandler>>();
                var result = await handler.HandleAsync(
                    new UnserveMockCommand(parseResult.GetRequiredValue(nameArg)),
                    ct
                );
                if (result is Result<UnservedMock, UnserveMockError>.Error err)
                {
                    return ServerCommand.Log(
                        err.Err,
                        logger,
                        UserFacingMessage(err.Err),
                        ExitCode(err.Err)
                    );
                }
                var unserved = ((Result<UnservedMock, UnserveMockError>.Ok)result).Value;
                Console.WriteLine(
                    unserved.Server is null
                        ? $"removed the mock device {unserved.Device.Value}."
                        : $"removed the mock device and server {unserved.Device.Value}."
                );
                return ExitCodeMapper.Success;
            }
        );
        return cmd;
    }

    private static string UserFacingMessage(UnserveMockError error) =>
        error switch
        {
            UnserveMockInvalidName n => DeviceNameMessage.Invalid(n.Raw),
            UnserveMockDeviceNotFound d => $"error: no device '{d.Name.Value}'.",
            UnserveMockDeviceConflict d =>
                $"error: device '{d.Name.Value}' is not a served mock: {d.Difference}. Nothing was removed.",
            UnserveMockServerConflict s =>
                $"error: server '{s.Name.Value}' is not a served mock: {s.Difference}. Nothing was removed; use `server` to change it.",
            UnserveMockServerRunning r =>
                $"error: server '{r.Name.Value}' is running (pid {r.ProcessId}); stop it with `server stop {r.Name.Value}` first.",
            UnserveMockRegistryFailure => "error: server process registry failure.",
            UnserveMockSessionFailure =>
                "error: session storage failed; if the device was removed, its scenario binding remains.",
            _ => "error: configuration storage failed.",
        };

    private static int ExitCode(UnserveMockError error) =>
        error switch
        {
            UnserveMockInvalidName or UnserveMockServerRunning => ExitCodeMapper.UsageError,
            UnserveMockDeviceNotFound or UnserveMockDeviceConflict or UnserveMockServerConflict =>
                ExitCodeMapper.DeviceError,
            _ => ExitCodeMapper.ConfigurationError,
        };
}
