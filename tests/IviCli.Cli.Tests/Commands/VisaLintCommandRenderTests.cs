using System.Collections.Immutable;
using System.Text.Json;
using IviCli.Application.Scripting;
using IviCli.Cli.Commands;
using Shouldly;

namespace IviCli.Cli.Tests.Commands;

public sealed class VisaLintCommandRenderTests
{
    [Fact]
    public void RenderPlain_emits_no_findings_line_for_empty_input()
    {
        var writer = new StringWriter();
        VisaLintCommand.RenderPlain("smoke.scpi", ImmutableArray<LintFinding>.Empty, writer);

        writer.ToString().ShouldBe("smoke.scpi: no findings" + Environment.NewLine);
    }

    [Fact]
    public void RenderPlain_includes_path_line_severity_message_and_snippet()
    {
        var writer = new StringWriter();
        var findings = ImmutableArray.Create(
            new LintFinding(7, LintSeverity.Warning, "unknown SCPI root: 'BOGUS'", "BOGUS:VOLT?")
        );

        VisaLintCommand.RenderPlain("smoke.scpi", findings, writer);

        var output = writer.ToString();
        output.ShouldContain("smoke.scpi:7:");
        output.ShouldContain("warning");
        output.ShouldContain("unknown SCPI root: 'BOGUS'");
        output.ShouldContain("BOGUS:VOLT?");
        output.ShouldContain("1 finding(s)");
    }

    [Fact]
    public void RenderJson_emits_a_parseable_array_with_normalised_keys()
    {
        var findings = ImmutableArray.Create(
            new LintFinding(3, LintSeverity.Warning, "unknown SCPI root: 'X'", "X:Y"),
            new LintFinding(5, LintSeverity.Error, "param out of range", "OUTP 99V")
        );

        var json = VisaLintCommand.RenderJson(findings);

        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement;
        arr.GetArrayLength().ShouldBe(2);
        arr[0].GetProperty("line").GetInt32().ShouldBe(3);
        arr[0].GetProperty("severity").GetString().ShouldBe("warning");
        arr[1].GetProperty("severity").GetString().ShouldBe("error");
    }
}
