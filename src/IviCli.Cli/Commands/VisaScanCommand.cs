using System.CommandLine;
using System.Globalization;
using IviCli.Application.Devices;
using IviCli.Domain;
using IviCli.Domain.Visa;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>Wires the <c>visa scan</c> subcommand.</summary>
public static class VisaScanCommand
{
    /// <summary>Builds the configured <see cref="Command"/>.</summary>
    public static Command Build(IServiceProvider services)
    {
        var jsonOpt = new Option<bool>("--json") { Description = "Emit machine-readable JSON." };

        var command = new Command(
            "scan",
            "Enumerate VISA resources visible to the registered backends."
        );
        command.Options.Add(jsonOpt);

        command.SetAction(
            async (parseResult, ct) =>
            {
                var json = parseResult.GetValue(jsonOpt);

                var handler = services.GetRequiredService<ScanDevicesQueryHandler>();
                var logger = services.GetRequiredService<ILogger<ScanDevicesQueryHandler>>();

                var result = await handler.HandleAsync(new ScanDevicesQuery(), ct);
                return result switch
                {
                    Result<ScanResult, ScanDevicesError>.Ok ok => Success(ok.Value, json),
                    Result<ScanResult, ScanDevicesError>.Error err => Fail(err.Err, logger),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );

        return command;
    }

    private static int Success(ScanResult scan, bool emitJson)
    {
        var inv = CultureInfo.InvariantCulture;
        if (emitJson)
        {
            Console.Write("{\"discovered\":[");
            for (var i = 0; i < scan.Resources.Length; i++)
            {
                if (i > 0)
                {
                    Console.Write(",");
                }
                var r = scan.Resources[i];
                var resourceString = r.Resource.ToLogString();
                var idnJson = r.Idn is null ? "null" : $"\"{Escape(r.Idn)}\"";
                Console.Write(
                    string.Create(
                        inv,
                        $"{{\"index\":{i + 1},\"resource\":\"{resourceString}\",\"idn\":{idnJson}}}"
                    )
                );
            }
            Console.WriteLine("]}");
        }
        else
        {
            if (scan.Resources.IsEmpty)
            {
                Console.WriteLine("(no resources discovered)");
            }
            else
            {
                for (var i = 0; i < scan.Resources.Length; i++)
                {
                    var r = scan.Resources[i];
                    Console.WriteLine(string.Create(inv, $"[{i + 1}]"));
                    Console.WriteLine(
                        string.Create(inv, $"    Resource: {r.Resource.ToLogString()}")
                    );
                    if (r.Idn is not null)
                    {
                        Console.WriteLine(string.Create(inv, $"    IDN: {r.Idn}"));
                    }
                }
            }
        }
        return ExitCodeMapper.Success;
    }

    private static int Fail(ScanDevicesError error, ILogger logger)
    {
        logger.Log(
            Logging.SerilogConfiguration.ToLogLevel(error.Severity),
            error.Cause,
            error.Message,
            error.LogArgs.ToArray()
        );
        Console.Error.WriteLine("error: scan failed.");
        return ExitCodeMapper.TransportError;
    }

    private static string Escape(string raw) =>
        raw.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
