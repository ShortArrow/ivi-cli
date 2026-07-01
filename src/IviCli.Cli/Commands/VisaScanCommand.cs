using System.Collections.Immutable;
using System.CommandLine;
using System.Globalization;
using IviCli.Application.Backends;
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
        var addOpt = new Option<bool>("--add")
        {
            Description =
                "Auto-register every discovered resource via `visa add` "
                + "(alias = host portion of the VISA resource). Existing names are skipped.",
        };
        var addTimeoutOpt = new Option<int>("--add-timeout-ms")
        {
            Description =
                "Per-device default operation timeout for auto-registered entries (default 3000).",
            DefaultValueFactory = _ => 3000,
        };
        var portOpt = new Option<int[]>("--port")
        {
            Description =
                "TCP-sweep the local subnet for raw-SOCKET instruments on this port "
                + "(repeatable, e.g. --port 5025 --port 1394). Also probed on every "
                + "discovered host. Without this flag only broadcast/mDNS discovery runs.",
            AllowMultipleArgumentsPerToken = true,
            DefaultValueFactory = _ => Array.Empty<int>(),
        };
        var subnetOpt = new Option<string?>("--subnet")
        {
            Description =
                "Sweep this CIDR (e.g. 192.168.3.0/24) instead of the auto-detected local subnets.",
        };
        var hostOpt = new Option<string?>("--host")
        {
            Description = "Probe only this single host (no subnet sweep).",
        };
        var verboseOpt = new Option<bool>("--verbose")
        {
            Description =
                "Send *IDN? to each discovered SOCKET endpoint to report the model, "
                + "and show diagnostics such as the resolved VXI-11 Core port.",
        };

        var command = new Command(
            "scan",
            "Enumerate VISA resources visible to the registered backends "
                + "(LXI mDNS + VXI-11 portmapper broadcast, plus optional --port TCP sweep, ADR 0008)."
        );
        command.Options.Add(jsonOpt);
        command.Options.Add(addOpt);
        command.Options.Add(addTimeoutOpt);
        command.Options.Add(portOpt);
        command.Options.Add(subnetOpt);
        command.Options.Add(hostOpt);
        command.Options.Add(verboseOpt);

        command.SetAction(
            async (parseResult, ct) =>
            {
                var json = parseResult.GetValue(jsonOpt);
                var add = parseResult.GetValue(addOpt);
                var addTimeoutMs = parseResult.GetValue(addTimeoutOpt);
                var options = new ScanOptions(
                    (parseResult.GetValue(portOpt) ?? []).ToImmutableArray(),
                    parseResult.GetValue(subnetOpt),
                    parseResult.GetValue(hostOpt),
                    parseResult.GetValue(verboseOpt)
                );

                var handler = services.GetRequiredService<ScanDevicesQueryHandler>();
                var logger = services.GetRequiredService<ILogger<ScanDevicesQueryHandler>>();

                var result = await handler.HandleAsync(new ScanDevicesQuery(options), ct);
                return result switch
                {
                    Result<ScanResult, ScanDevicesError>.Ok ok => await SuccessAsync(
                        ok.Value,
                        json,
                        add,
                        addTimeoutMs,
                        services,
                        ct
                    ),
                    Result<ScanResult, ScanDevicesError>.Error err => Fail(err.Err, logger),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );

        return command;
    }

    private static async Task<int> SuccessAsync(
        ScanResult scan,
        bool emitJson,
        bool autoAdd,
        int addTimeoutMs,
        IServiceProvider services,
        CancellationToken ct
    )
    {
        var exit = Success(scan, emitJson);
        if (!autoAdd || scan.Resources.IsEmpty)
        {
            return exit;
        }

        var addHandler = services.GetRequiredService<AddDeviceCommandHandler>();
        var added = 0;
        var skipped = 0;
        foreach (var r in scan.Resources)
        {
            var alias = DeriveAlias(r.Resource);
            var addResult = await addHandler.HandleAsync(
                new AddDeviceCommand(alias, FormatResource(r.Resource), addTimeoutMs),
                ct
            );
            switch (addResult)
            {
                case Result<Domain.Devices.DeviceName, AddDeviceError>.Ok ok:
                    Console.WriteLine($"  added: {ok.Value.Value} → {FormatResource(r.Resource)}");
                    added++;
                    break;
                case Result<Domain.Devices.DeviceName, AddDeviceError>.Error err
                    when err.Err is AddDeviceNameTaken:
                    Console.WriteLine($"  skipped (alias taken): {alias}");
                    skipped++;
                    break;
                case Result<Domain.Devices.DeviceName, AddDeviceError>.Error err:
                    Console.Error.WriteLine($"  failed to add {alias}: {err.Err.Message}");
                    break;
            }
        }
        Console.WriteLine($"auto-add: {added} added, {skipped} skipped.");
        return exit;
    }

    /// <summary>
    /// Derives a CLI alias from a discovered resource. Preference order:
    /// <list type="number">
    /// <item>TCPIP host portion lowercased, hyphenated.</item>
    /// <item>USB serial number.</item>
    /// <item>GPIB primary address.</item>
    /// </list>
    /// The alias is intentionally deterministic so re-running <c>scan --add</c>
    /// produces the same name and the second attempt cleanly skips.
    /// </summary>
    public static string DeriveAlias(VisaResource resource) =>
        resource switch
        {
            VisaResource.Tcpip t => Sanitize(t.Host),
            VisaResource.TcpipSocket s => $"{Sanitize(s.Host)}-{s.Port}",
            VisaResource.Usb u => $"usb-{Sanitize(u.SerialNumber)}",
            VisaResource.Gpib g => $"gpib-{g.PrimaryAddress}",
            _ => "device",
        };

    /// <summary>
    /// Renders an unmasked VISA resource string suitable for
    /// <see cref="AddDeviceCommand"/>. Delegates to
    /// <see cref="VisaResource.ToCanonical"/> so the value the CLI emits
    /// round-trips through the parser unchanged.
    /// </summary>
    public static string FormatResource(VisaResource resource) => resource.ToCanonical();

    private static string Sanitize(string raw)
    {
        var lower = raw.ToLowerInvariant();
        var safe = new string(
            lower.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray()
        );
        return safe.Trim('-');
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
                var resourceString = FormatResource(r.Resource);
                var idnJson = r.Idn is null ? "null" : $"\"{Escape(r.Idn)}\"";
                var detailJson = r.Detail is null ? "null" : $"\"{Escape(r.Detail)}\"";
                Console.Write(
                    string.Create(
                        inv,
                        $"{{\"index\":{i + 1},\"resource\":\"{resourceString}\",\"idn\":{idnJson},\"detail\":{detailJson}}}"
                    )
                );
            }
            Console.WriteLine("]}");
        }
        else if (scan.Resources.IsEmpty)
        {
            Console.WriteLine("(no resources discovered)");
        }
        else
        {
            var groups = scan
                .Resources.GroupBy(GroupKey, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            for (var i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                Console.WriteLine(string.Create(inv, $"[{i + 1}] {group.Key}"));
                foreach (
                    var r in group.OrderBy(r => FormatResource(r.Resource), StringComparer.Ordinal)
                )
                {
                    var line = $"      {FormatResource(r.Resource)}";
                    if (r.Idn is not null)
                    {
                        line += $"   [{r.Idn}]";
                    }
                    if (r.Detail is not null)
                    {
                        line += $"   ({r.Detail})";
                    }
                    Console.WriteLine(line);
                }
            }
        }
        return ExitCodeMapper.Success;
    }

    /// <summary>
    /// Groups discovered resources by the endpoint they share: the host for
    /// TCPIP-family resources (so a device's VXI-11 / HiSLIP / SCPI-RAW access
    /// paths list together), otherwise the resource string itself.
    /// </summary>
    private static string GroupKey(DiscoveredResource r) =>
        r.Resource switch
        {
            VisaResource.Tcpip t => t.Host,
            VisaResource.TcpipSocket s => s.Host,
            _ => FormatResource(r.Resource),
        };

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
