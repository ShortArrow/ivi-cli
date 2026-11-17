using System.Collections.Immutable;
using System.Text.Json;
using IviCli.Application.Diagnostics;
using IviCli.Cli.Commands;
using Shouldly;

namespace IviCli.Cli.Tests.Commands;

/// <summary>
/// Locks the <c>doctor --json</c> stdout contract: a <c>checks</c> array of
/// name, status and detail.
/// </summary>
public sealed class DiagnosticsReportFormatterTests
{
    [Fact]
    public void FormatJson_writes_each_check_with_its_status_label()
    {
        var report = new DiagnosticsReport(
            ImmutableArray.Create(
                new DiagnosticCheck("runtime", DiagnosticStatus.Ok, ".NET 10.0.0"),
                new DiagnosticCheck("config", DiagnosticStatus.Warning, "no devices"),
                new DiagnosticCheck("visa", DiagnosticStatus.Error, "not installed")
            )
        );

        DiagnosticsReportFormatter
            .FormatJson(report)
            .ShouldBe(
                """{"checks":[{"name":"runtime","status":"ok","detail":".NET 10.0.0"},{"name":"config","status":"warning","detail":"no devices"},{"name":"visa","status":"error","detail":"not installed"}]}"""
            );
    }

    [Fact]
    public void FormatJson_escapes_control_characters_quotes_and_backslashes()
    {
        const string detail = "C:\\dir \"q\"\r\nline2\u0007\tend\u007f";
        var report = new DiagnosticsReport(
            ImmutableArray.Create(new DiagnosticCheck("visa", DiagnosticStatus.Error, detail))
        );

        var json = DiagnosticsReportFormatter.FormatJson(report);

        json.ShouldContain(@"""detail"":""C:\\dir \""q\""\r\nline2\u0007\tend", Case.Sensitive);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("checks")[0].GetProperty("detail").GetString().ShouldBe(detail);
    }

    [Fact]
    public void FormatJson_writes_an_empty_array_when_there_are_no_checks()
    {
        DiagnosticsReportFormatter
            .FormatJson(new DiagnosticsReport(ImmutableArray<DiagnosticCheck>.Empty))
            .ShouldBe("""{"checks":[]}""");
    }
}
