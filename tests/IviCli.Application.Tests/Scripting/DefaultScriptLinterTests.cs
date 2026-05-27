using IviCli.Application.Scripting;
using IviCli.Domain;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Application.Tests.Scripting;

public sealed class DefaultScriptLinterTests
{
    private static ScpiScript Parse(string source) => ScpiScript.Parse(source).ShouldBeOk();

    [Fact]
    public async Task LintAsync_returns_no_findings_for_empty_script()
    {
        var linter = new DefaultScriptLinter();
        var script = Parse("");

        var findings = await linter.LintAsync(script, default);

        findings.ShouldBeEmpty();
    }

    [Fact]
    public async Task LintAsync_passes_known_common_commands_and_core_roots()
    {
        var linter = new DefaultScriptLinter();
        var script = Parse(
            string.Join(
                '\n',
                "*IDN?",
                "*RST",
                "MEAS:VOLT:DC?",
                "SENSe:CURR?",
                "OUTP ON",
                ":SYST:ERR?"
            )
        );

        var findings = await linter.LintAsync(script, default);

        findings.ShouldBeEmpty();
    }

    [Fact]
    public async Task LintAsync_flags_unknown_root_with_warning()
    {
        var linter = new DefaultScriptLinter();
        var script = Parse("NOTAROOT:VOLT?");

        var findings = await linter.LintAsync(script, default);

        var only = findings.ShouldHaveSingleItem();
        only.Line.ShouldBe(1);
        only.Severity.ShouldBe(LintSeverity.Warning);
        only.Message.ShouldContain("NOTAROOT");
        only.Snippet.ShouldBe("NOTAROOT:VOLT?");
    }

    [Fact]
    public async Task LintAsync_only_surfaces_unknown_rows_in_mixed_script()
    {
        var linter = new DefaultScriptLinter();
        var script = Parse(string.Join('\n', "*IDN?", "BOGUS:READ?", "MEAS:VOLT?", "NOPE:WHAT?"));

        var findings = await linter.LintAsync(script, default);

        findings.Length.ShouldBe(2);
        findings[0].Line.ShouldBe(2);
        findings[0].Message.ShouldContain("BOGUS");
        findings[1].Line.ShouldBe(4);
        findings[1].Message.ShouldContain("NOPE");
    }

    [Fact]
    public async Task LintAsync_never_flags_control_directives()
    {
        var linter = new DefaultScriptLinter();
        var script = Parse(string.Join('\n', "sleep 100", "assert FAKE,.*", "echo hello world"));

        var findings = await linter.LintAsync(script, default);

        findings.ShouldBeEmpty();
    }

    [Fact]
    public async Task LintAsync_truncates_long_snippets_with_ellipsis()
    {
        var linter = new DefaultScriptLinter();
        var longText = "BOGUS:" + new string('X', 200);
        var script = Parse(longText);

        var findings = await linter.LintAsync(script, default);

        var only = findings.ShouldHaveSingleItem();
        only.Snippet.Length.ShouldBe(80);
        only.Snippet.ShouldEndWith("…");
    }
}
