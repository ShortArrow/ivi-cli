using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using IviCli.Application.Audit;
using IviCli.Infrastructure.Audit;
using Shouldly;

namespace IviCli.Infrastructure.Tests.Audit;

public sealed class NdjsonAuditLogTests
{
    private const string Path = "/var/log/ivi/audit.ndjson";

    [Fact]
    public async Task AppendAsync_writes_one_line_per_event()
    {
        var fs = new MockFileSystem();
        using var sut = new NdjsonAuditLog(fs, Path);

        await sut.AppendAsync(
            new AuthSucceeded(
                new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
                Mechanism: "pat",
                Subject: "lab-dashboard",
                Transport: "http"
            ),
            default
        );
        await sut.AppendAsync(
            new ApiRequest(
                new DateTimeOffset(2026, 1, 2, 3, 4, 6, TimeSpan.Zero),
                Method: "POST",
                Path: "/v1/devices/psu1/query",
                Status: 200,
                Subject: "lab-dashboard",
                LatencyMs: 42
            ),
            default
        );

        var contents = fs.File.ReadAllText(Path);
        var lines = contents.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(2);
        // Each line must be valid JSON.
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            doc.RootElement.TryGetProperty("kind", out _).ShouldBeTrue();
            doc.RootElement.TryGetProperty("timestamp", out _).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task AppendAsync_flattens_AuthFailed_fields()
    {
        var fs = new MockFileSystem();
        using var sut = new NdjsonAuditLog(fs, Path);

        await sut.AppendAsync(
            new AuthFailed(
                DateTimeOffset.UtcNow,
                Mechanism: "pat",
                Reason: "invalid_token",
                Transport: "websocket"
            ),
            default
        );

        using var doc = JsonDocument.Parse(fs.File.ReadAllText(Path).TrimEnd());
        doc.RootElement.GetProperty("kind").GetString().ShouldBe("auth.failed");
        doc.RootElement.GetProperty("mechanism").GetString().ShouldBe("pat");
        doc.RootElement.GetProperty("reason").GetString().ShouldBe("invalid_token");
        doc.RootElement.GetProperty("transport").GetString().ShouldBe("websocket");
    }

    [Fact]
    public async Task AppendAsync_creates_parent_directory()
    {
        var fs = new MockFileSystem();
        using var sut = new NdjsonAuditLog(fs, Path);

        await sut.AppendAsync(
            new ServerLifecycle(DateTimeOffset.UtcNow, "hislip-srv", "start"),
            default
        );

        fs.File.Exists(Path).ShouldBeTrue();
    }

    [Fact]
    public async Task AppendAsync_round_trips_ConfigMutated_subject()
    {
        var fs = new MockFileSystem();
        using var sut = new NdjsonAuditLog(fs, Path);

        await sut.AppendAsync(
            new ConfigMutated(
                new DateTimeOffset(2026, 5, 29, 10, 0, 0, TimeSpan.Zero),
                Operation: "device.add",
                Target: "psu1",
                Subject: "cli/alice"
            ),
            default
        );

        using var doc = JsonDocument.Parse(fs.File.ReadAllText(Path).TrimEnd());
        doc.RootElement.GetProperty("kind").GetString().ShouldBe("config.mutated");
        doc.RootElement.GetProperty("operation").GetString().ShouldBe("device.add");
        doc.RootElement.GetProperty("target").GetString().ShouldBe("psu1");
        doc.RootElement.GetProperty("subject").GetString().ShouldBe("cli/alice");
    }

    [Fact]
    public async Task AppendAsync_round_trips_ServerLifecycle_subject()
    {
        var fs = new MockFileSystem();
        using var sut = new NdjsonAuditLog(fs, Path);

        await sut.AppendAsync(
            new ServerLifecycle(
                new DateTimeOffset(2026, 5, 29, 10, 0, 0, TimeSpan.Zero),
                Server: "gw1",
                Action: "crashed",
                Subject: "cli/bob"
            ),
            default
        );

        using var doc = JsonDocument.Parse(fs.File.ReadAllText(Path).TrimEnd());
        doc.RootElement.GetProperty("kind").GetString().ShouldBe("server.lifecycle");
        doc.RootElement.GetProperty("server").GetString().ShouldBe("gw1");
        doc.RootElement.GetProperty("action").GetString().ShouldBe("crashed");
        doc.RootElement.GetProperty("subject").GetString().ShouldBe("cli/bob");
    }

    [Fact]
    public async Task Concurrent_appends_do_not_interleave_lines()
    {
        var fs = new MockFileSystem();
        using var sut = new NdjsonAuditLog(fs, Path);

        var tasks = Enumerable
            .Range(0, 20)
            .Select(i =>
                sut.AppendAsync(
                    new ConfigMutated(
                        DateTimeOffset.UtcNow,
                        Operation: $"op_{i}",
                        Target: $"target_{i}"
                    ),
                    default
                )
            )
            .ToArray();
        await Task.WhenAll(tasks);

        var contents = fs.File.ReadAllText(Path);
        var lines = contents.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(20);
        foreach (var line in lines)
        {
            JsonDocument.Parse(line); // shouldn't throw
        }
    }
}
