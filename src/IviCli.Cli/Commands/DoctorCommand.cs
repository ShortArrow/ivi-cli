using System.CommandLine;
using System.Globalization;
using IviCli.Application.Diagnostics;
using IviCli.Application.Logging;
using IviCli.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IviCli.Cli.Commands;

/// <summary>
/// Wires the <c>ivicli doctor</c> top-level subcommand.
/// </summary>
public static class DoctorCommand
{
    /// <summary>Builds the configured <see cref="Command"/>.</summary>
    public static Command Build(IServiceProvider services)
    {
        var jsonOpt = new Option<bool>("--json") { Description = "Emit machine-readable JSON." };

        var command = new Command(
            "doctor",
            "Report runtime, configuration, and backend-registration health."
        );
        command.Options.Add(jsonOpt);

        command.SetAction(
            async (parseResult, ct) =>
            {
                var json = parseResult.GetValue(jsonOpt);
                var handler = services.GetRequiredService<DoctorQueryHandler>();
                var logger = services.GetRequiredService<ILogger<DoctorQueryHandler>>();

                var result = await handler.HandleAsync(new DoctorQuery(), ct);
                return result switch
                {
                    Result<DiagnosticsReport, DoctorError>.Ok ok => Render(ok.Value, json),
                    Result<DiagnosticsReport, DoctorError>.Error err => Fail(err.Err, logger),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );

        return command;
    }

    private static int Render(DiagnosticsReport report, bool emitJson)
    {
        var inv = CultureInfo.InvariantCulture;
        if (emitJson)
        {
            Console.Write("{\"checks\":[");
            for (var i = 0; i < report.Checks.Length; i++)
            {
                if (i > 0)
                {
                    Console.Write(",");
                }
                var c = report.Checks[i];
                Console.Write(
                    string.Create(
                        inv,
                        $"{{\"name\":\"{c.Name}\",\"status\":\"{StatusLabel(c.Status)}\",\"detail\":\"{Escape(c.Detail)}\"}}"
                    )
                );
            }
            Console.WriteLine("]}");
        }
        else
        {
            foreach (var c in report.Checks)
            {
                Console.WriteLine(
                    string.Create(inv, $"[{StatusGlyph(c.Status)}] {c.Name, -12} {c.Detail}")
                );
            }
        }

        return report.Checks.Any(c => c.Status == DiagnosticStatus.Error)
            ? ExitCodeMapper.GenericFailure
            : ExitCodeMapper.Success;
    }

    private static int Fail(DoctorError error, ILogger logger)
    {
        logger.LogIviError(error);
        Console.Error.WriteLine("error: doctor failed.");
        return ExitCodeMapper.GenericFailure;
    }

    private static string StatusGlyph(DiagnosticStatus status) =>
        status switch
        {
            DiagnosticStatus.Ok => "OK ",
            DiagnosticStatus.Warning => "WRN",
            DiagnosticStatus.Error => "ERR",
            _ => "?  ",
        };

    private static string StatusLabel(DiagnosticStatus status) =>
        status switch
        {
            DiagnosticStatus.Ok => "ok",
            DiagnosticStatus.Warning => "warning",
            DiagnosticStatus.Error => "error",
            _ => "unknown",
        };

    private static string Escape(string raw) =>
        raw.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
