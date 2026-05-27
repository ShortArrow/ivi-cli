using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using IviCli.Application.Capture;
using IviCli.Infrastructure.Capture;
using Shouldly;

namespace IviCli.Infrastructure.Tests.Capture;

public sealed class NdjsonTrafficWriterTests
{
    private const string Path = "/var/log/ivi-cli/run.ndjson";

    private static TrafficEvent Event(
        TrafficOp op,
        string device = "psu1",
        string? data = null,
        string? response = null,
        bool ok = true,
        int? latency = null,
        string? error = null
    ) =>
        new(
            new DateTimeOffset(2026, 5, 27, 12, 0, 0, TimeSpan.Zero),
            device,
            op,
            data,
            response,
            ok,
            latency,
            error
        );

    [Fact]
    public async Task AppendAsync_writes_one_json_line_with_trailing_newline()
    {
        var fs = new MockFileSystem();
        var writer = new NdjsonTrafficWriter(fs, Path);
        await writer.AppendAsync(
            Event(TrafficOp.Query, data: "*IDN?", response: "ACME,PSU,1.0", latency: 12),
            default
        );

        var content = fs.File.ReadAllText(Path);
        content.ShouldEndWith("\n");
        content.Count(c => c == '\n').ShouldBe(1);

        var line = content.TrimEnd('\n');
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        root.GetProperty("device").GetString().ShouldBe("psu1");
        root.GetProperty("op").GetString().ShouldBe("Query");
        root.GetProperty("data").GetString().ShouldBe("*IDN?");
        root.GetProperty("response").GetString().ShouldBe("ACME,PSU,1.0");
        root.GetProperty("ok").GetBoolean().ShouldBeTrue();
        root.GetProperty("latencyMs").GetInt32().ShouldBe(12);
    }

    [Fact]
    public async Task AppendAsync_three_events_yield_three_lines_in_order()
    {
        var fs = new MockFileSystem();
        var writer = new NdjsonTrafficWriter(fs, Path);
        await writer.AppendAsync(Event(TrafficOp.Open), default);
        await writer.AppendAsync(Event(TrafficOp.Write, data: "OUTP ON"), default);
        await writer.AppendAsync(Event(TrafficOp.Close), default);

        var lines = fs.File.ReadAllText(Path).TrimEnd('\n').Split('\n');
        lines.Length.ShouldBe(3);
        JsonDocument.Parse(lines[0]).RootElement.GetProperty("op").GetString().ShouldBe("Open");
        JsonDocument.Parse(lines[1]).RootElement.GetProperty("op").GetString().ShouldBe("Write");
        JsonDocument.Parse(lines[2]).RootElement.GetProperty("op").GetString().ShouldBe("Close");
    }

    [Fact]
    public async Task AppendAsync_serialises_concurrent_writes_without_interleaving()
    {
        var fs = new MockFileSystem();
        var writer = new NdjsonTrafficWriter(fs, Path);
        var tasks = Enumerable
            .Range(0, 50)
            .Select(i => writer.AppendAsync(Event(TrafficOp.Write, data: $"CMD{i}"), default))
            .ToArray();
        await Task.WhenAll(tasks);

        var lines = fs.File.ReadAllText(Path).TrimEnd('\n').Split('\n');
        lines.Length.ShouldBe(50);
        foreach (var line in lines)
        {
            // Each line must be a single valid JSON document.
            using var doc = JsonDocument.Parse(line);
            doc.RootElement.GetProperty("op").GetString().ShouldBe("Write");
        }
    }

    [Fact]
    public async Task AppendAsync_creates_parent_directory_if_missing()
    {
        var fs = new MockFileSystem();
        var nestedPath = "/var/log/ivi-cli/deeper/run.ndjson";
        var writer = new NdjsonTrafficWriter(fs, nestedPath);
        await writer.AppendAsync(Event(TrafficOp.Open), default);

        fs.File.Exists(nestedPath).ShouldBeTrue();
    }
}
