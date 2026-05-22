using System.Collections.Immutable;
using System.IO.Abstractions;
using IviCli.Application.Backends;
using IviCli.Domain;

namespace IviCli.Application.Diagnostics;

/// <summary>
/// Application-layer handler for <c>ivicli diagnose</c> (PRD §6.4). Reports
/// runtime, config, and backend-registration health. Since each check is
/// reported independently, the handler does not fail at the command level;
/// even when every check is in Error state the caller still receives a
/// populated <see cref="DiagnosticsReport"/>.
/// </summary>
public sealed class DiagnoseQueryHandler
{
    private readonly IFileSystem _fs;
    private readonly IEnumerable<IIviBackend> _backends;
    private readonly IEnumerable<IBackendScanner> _scanners;
    private readonly string _configPath;
    private readonly string _logDirectory;

    /// <summary>Creates a new handler.</summary>
    public DiagnoseQueryHandler(
        IFileSystem fs,
        IEnumerable<IIviBackend> backends,
        IEnumerable<IBackendScanner> scanners,
        DiagnoseHandlerOptions options
    )
    {
        _fs = fs;
        _backends = backends;
        _scanners = scanners;
        _configPath = options.ConfigPath;
        _logDirectory = options.LogDirectory;
    }

    /// <summary>Runs every diagnostic check and aggregates them.</summary>
    public Task<Result<DiagnosticsReport, DiagnoseError>> HandleAsync(
        DiagnoseQuery query,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();

        var checks = ImmutableArray.CreateBuilder<DiagnosticCheck>();

        checks.Add(CheckDotnetRuntime());
        checks.Add(CheckPlatform());
        checks.Add(CheckConfigPath());
        checks.Add(CheckLogDirectory());
        checks.Add(CheckBackends());
        checks.Add(CheckScanners());

        return Task.FromResult(
            Result.Success<DiagnosticsReport, DiagnoseError>(
                new DiagnosticsReport(checks.ToImmutable())
            )
        );
    }

    private static DiagnosticCheck CheckDotnetRuntime()
    {
        var version = Environment.Version;
        return new DiagnosticCheck("dotnet", DiagnosticStatus.Ok, $"runtime {version}");
    }

    private static DiagnosticCheck CheckPlatform()
    {
        var description = Environment.OSVersion.ToString();
        return new DiagnosticCheck("platform", DiagnosticStatus.Ok, description);
    }

    private DiagnosticCheck CheckConfigPath()
    {
        if (_fs.File.Exists(_configPath))
        {
            return new DiagnosticCheck("config", DiagnosticStatus.Ok, $"{_configPath} (exists)");
        }
        return new DiagnosticCheck(
            "config",
            DiagnosticStatus.Warning,
            $"{_configPath} (will be created on first write)"
        );
    }

    private DiagnosticCheck CheckLogDirectory()
    {
        if (_fs.Directory.Exists(_logDirectory))
        {
            return new DiagnosticCheck("logs", DiagnosticStatus.Ok, $"{_logDirectory} (exists)");
        }
        return new DiagnosticCheck(
            "logs",
            DiagnosticStatus.Warning,
            $"{_logDirectory} (will be created on first write)"
        );
    }

    private DiagnosticCheck CheckBackends()
    {
        var count = _backends.Count();
        return count switch
        {
            0 => new DiagnosticCheck(
                "backends",
                DiagnosticStatus.Error,
                "no IIviBackend implementation registered — visa operations will fail"
            ),
            _ => new DiagnosticCheck(
                "backends",
                DiagnosticStatus.Ok,
                $"{count} IIviBackend implementation(s) registered"
            ),
        };
    }

    private DiagnosticCheck CheckScanners()
    {
        var count = _scanners.Count();
        return count switch
        {
            0 => new DiagnosticCheck(
                "scanners",
                DiagnosticStatus.Warning,
                "no IBackendScanner registered — visa scan will report nothing"
            ),
            _ => new DiagnosticCheck(
                "scanners",
                DiagnosticStatus.Ok,
                $"{count} IBackendScanner implementation(s) registered"
            ),
        };
    }
}

/// <summary>Composition-time options for <see cref="DiagnoseQueryHandler"/>.</summary>
/// <param name="ConfigPath">Absolute path to the config.toml file.</param>
/// <param name="LogDirectory">Absolute path to the log output directory.</param>
public sealed record DiagnoseHandlerOptions(string ConfigPath, string LogDirectory);
