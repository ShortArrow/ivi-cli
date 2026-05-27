using System.IO.Abstractions.TestingHelpers;
using IviCli.Application.Capture;
using IviCli.Infrastructure.Capture;
using Shouldly;

namespace IviCli.Infrastructure.Tests.Capture;

public sealed class NdjsonTrafficReaderTests
{
    private const string Path = "/var/log/ivi-cli/run.ndjson";

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(
        System.Text.Json.JsonSerializerDefaults.Web
    )
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private static TrafficEvent Event(
        TrafficOp op,
        string device = "psu1",
        string? data = null,
        string? response = null,
        bool ok = true
    ) =>
        new(
            new DateTimeOffset(2026, 5, 27, 12, 0, 0, TimeSpan.Zero),
            device,
            op,
            data,
            response,
            ok,
            LatencyMs: ok ? 5 : null,
            Error: ok ? null : "boom"
        );

    [Fact]
    public async Task ReadAsync_round_trips_with_NdjsonTrafficWriter()
    {
        var fs = new MockFileSystem();
        var writer = new NdjsonTrafficWriter(fs, Path);
        await writer.AppendAsync(Event(TrafficOp.Open), default);
        await writer.AppendAsync(Event(TrafficOp.Query, data: "*IDN?", response: "ACME"), default);
        await writer.AppendAsync(Event(TrafficOp.Close), default);

        var reader = new NdjsonTrafficReader(fs);
        var events = new List<TrafficEvent>();
        await foreach (var ev in reader.ReadAsync(Path, default))
        {
            events.Add(ev);
        }

        events.Count.ShouldBe(3);
        events[0].Op.ShouldBe(TrafficOp.Open);
        events[1].Op.ShouldBe(TrafficOp.Query);
        events[1].Data.ShouldBe("*IDN?");
        events[1].Response.ShouldBe("ACME");
        events[2].Op.ShouldBe(TrafficOp.Close);
    }

    [Fact]
    public async Task ReadAsync_skips_blank_lines_and_hash_comments()
    {
        var fs = new MockFileSystem();
        var ev = Event(TrafficOp.Write, data: "OUTP ON");
        var jsonLine = System.Text.Json.JsonSerializer.Serialize(ev, JsonOptions);
        fs.AddFile(
            Path,
            new MockFileData(
                "# header comment\n"
                    + "\n"
                    + jsonLine
                    + "\n"
                    + "   # indented comment\n"
                    + jsonLine
                    + "\n"
            )
        );

        var reader = new NdjsonTrafficReader(fs);
        var events = new List<TrafficEvent>();
        await foreach (var read in reader.ReadAsync(Path, default))
        {
            events.Add(read);
        }

        events.Count.ShouldBe(2);
        events.ShouldAllBe(e => e.Op == TrafficOp.Write && e.Data == "OUTP ON");
    }

    [Fact]
    public async Task ReadAsync_throws_InvalidDataException_with_line_number_on_malformed_json()
    {
        var fs = new MockFileSystem();
        var ev = System.Text.Json.JsonSerializer.Serialize(
            Event(TrafficOp.Write, data: "X"),
            JsonOptions
        );
        fs.AddFile(Path, new MockFileData(ev + "\n{not valid json\n"));

        var reader = new NdjsonTrafficReader(fs);
        var ex = await Should.ThrowAsync<InvalidDataException>(async () =>
        {
            await foreach (var _ in reader.ReadAsync(Path, default))
            {
                // keep reading until the bad line.
            }
        });
        ex.Message.ShouldContain(":2:");
    }

    [Fact]
    public async Task ReadAsync_throws_when_file_is_missing()
    {
        var fs = new MockFileSystem();
        // Ensure the parent directory exists so the MockFileSystem surfaces a
        // FileNotFoundException rather than a DirectoryNotFoundException — the
        // caller cares only that *some* IO exception bubbles up.
        fs.AddDirectory("/var/log/ivi-cli");
        var reader = new NdjsonTrafficReader(fs);

        await Should.ThrowAsync<FileNotFoundException>(async () =>
        {
            await foreach (var _ in reader.ReadAsync(Path, default))
            {
                // never reached.
            }
        });
    }
}
