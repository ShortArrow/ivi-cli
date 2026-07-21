using System.Collections.Immutable;
using System.Text.Json;
using IviCli.Application.Capture;
using IviCli.Cli.Commands;
using Shouldly;

namespace IviCli.Cli.Tests.Commands;

public sealed class MockReceivedWritesCommandRenderTests
{
    private static TrafficEvent Write(string scpi) =>
        new(
            default,
            "dut",
            TrafficOp.Write,
            scpi,
            Response: null,
            Ok: true,
            LatencyMs: null,
            Error: null
        );

    [Fact]
    public void Default_prints_only_the_last_write_and_succeeds()
    {
        var writer = new StringWriter();
        var writes = ImmutableArray.Create(Write(":VOLT 1.000"), Write(":VOLT 24.000"));

        var code = MockReceivedWritesCommand.Render(writes, all: false, json: false, writer);

        code.ShouldBe(0);
        writer.ToString().ShouldBe(":VOLT 24.000" + Environment.NewLine);
    }

    [Fact]
    public void All_prints_every_write_in_order()
    {
        var writer = new StringWriter();
        var writes = ImmutableArray.Create(Write(":VOLT 1.000"), Write(":VOLT 24.000"));

        var code = MockReceivedWritesCommand.Render(writes, all: true, json: false, writer);

        code.ShouldBe(0);
        var lines = writer
            .ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var expected = new[] { ":VOLT 1.000", ":VOLT 24.000" };
        lines.ShouldBe(expected);
    }

    [Fact]
    public void Empty_writes_nothing_and_returns_nonzero()
    {
        var writer = new StringWriter();

        var code = MockReceivedWritesCommand.Render(
            ImmutableArray<TrafficEvent>.Empty,
            all: false,
            json: false,
            writer
        );

        code.ShouldBe(1);
        writer.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Json_default_emits_the_last_write_as_a_parseable_object()
    {
        var writer = new StringWriter();
        var writes = ImmutableArray.Create(Write(":VOLT 1.000"), Write(":VOLT 24.000"));

        MockReceivedWritesCommand.Render(writes, all: false, json: true, writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        doc.RootElement.GetProperty("scpi").GetString().ShouldBe(":VOLT 24.000");
        doc.RootElement.GetProperty("device").GetString().ShouldBe("dut");
    }

    [Fact]
    public void Json_all_emits_a_parseable_array()
    {
        var writer = new StringWriter();
        var writes = ImmutableArray.Create(Write(":VOLT 1.000"), Write(":VOLT 24.000"));

        MockReceivedWritesCommand.Render(writes, all: true, json: true, writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        doc.RootElement.GetArrayLength().ShouldBe(2);
        doc.RootElement[1].GetProperty("scpi").GetString().ShouldBe(":VOLT 24.000");
    }
}
