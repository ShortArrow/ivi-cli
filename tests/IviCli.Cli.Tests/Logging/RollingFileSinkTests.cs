using System.Text.Json;
using System.Text.RegularExpressions;
using IviCli.Cli.Logging;
using Serilog.Events;
using Shouldly;
using Xunit;

namespace IviCli.Cli.Tests.Logging;

/// <summary>
/// Characterises what the rolling file sink puts on disk: one file per day,
/// named from the configured path with the day appended, holding one Compact
/// JSON event per line. Anyone tailing or shipping these logs depends on all
/// three, so a sink upgrade that changes any of them is a breaking change.
/// </summary>
public sealed class RollingFileSinkTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "ivicli-sink-" + Guid.NewGuid().ToString("N")
    );

    [Fact]
    public void Writes_the_day_s_file_as_one_compact_json_event_per_line()
    {
        var logger = SerilogConfiguration.Build(
            new SerilogConfiguration.Options(
                MinimumLevel: LogEventLevel.Information,
                ConsoleMinimumLevel: LogEventLevel.Fatal,
                ConsoleJsonFormat: false,
                LogFileOverride: Path.Combine(_directory, "ivi-cli-.log")
            )
        );
        logger.Information("scenario {Scenario} activated", "my_dmm");
        (logger as IDisposable)?.Dispose();

        var file = Directory.GetFiles(_directory).ShouldHaveSingleItem();
        Regex.IsMatch(Path.GetFileName(file), @"^ivi-cli-\d{8}\.log$").ShouldBeTrue();

        var line = File.ReadAllLines(file).ShouldHaveSingleItem();
        using var evt = JsonDocument.Parse(line);
        evt.RootElement.GetProperty("@mt").GetString().ShouldBe("scenario {Scenario} activated");
        evt.RootElement.GetProperty("Scenario").GetString().ShouldBe("my_dmm");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
