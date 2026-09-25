using IviCli.Application.Scripting;
using IviCli.Domain;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Application.Tests.Scripting;

public class ScpiScriptTests
{
    [Fact]
    public void Parse_classifies_query_when_line_ends_with_question_mark()
    {
        var script = ScpiScript.Parse("*IDN?").ShouldBeOk();
        script.Directives.Length.ShouldBe(1);
        script.Directives[0].ShouldBeOfType<ScpiScriptDirective.Query>().Text.ShouldBe("*IDN?");
    }

    [Theory]
    [InlineData("MEAS:VOLT? CH1")]
    [InlineData("MEAS:VOLT? (@1)")]
    [InlineData("VOLT 1;MEAS:VOLT? (@1)")]
    public void Parse_classifies_query_when_a_header_ends_with_question_mark(string line)
    {
        var script = ScpiScript.Parse(line).ShouldBeOk();
        script.Directives[0].ShouldBeOfType<ScpiScriptDirective.Query>().Text.ShouldBe(line);
    }

    [Fact]
    public void Parse_classifies_write_when_line_lacks_question_mark()
    {
        var script = ScpiScript.Parse("*RST").ShouldBeOk();
        script.Directives.Length.ShouldBe(1);
        script.Directives[0].ShouldBeOfType<ScpiScriptDirective.Write>().Text.ShouldBe("*RST");
    }

    [Theory]
    [InlineData("!sleep 250")]
    [InlineData("!SLEEP 250")]
    public void Parse_reads_bang_sleep_as_sleep_directive(string line)
    {
        var script = ScpiScript.Parse(line).ShouldBeOk();
        var s = script
            .Directives.ShouldHaveSingleItem()
            .ShouldBeOfType<ScpiScriptDirective.Sleep>();
        s.Duration.ShouldBe(TimeSpan.FromMilliseconds(250));
    }

    [Theory]
    [InlineData("!sleep -1")]
    [InlineData("!wait 5")]
    public void Parse_rejects_invalid_bang_directive_at_its_line(string line)
    {
        var result = ScpiScript.Parse(line);
        var error = result.ShouldBeOfType<Result<ScpiScript, ScpiScriptError>.Error>();
        error.Err.ShouldBeOfType<ScpiScriptInvalidDirective>().Line.ShouldBe(1);
    }

    [Theory]
    [InlineData("!assert ^FAKE,.*$", "^FAKE,.*$")]
    [InlineData("!assert ^#15", "^#15")]
    public void Parse_reads_bang_assert_whole_line_as_pattern(string line, string pattern)
    {
        var script = ScpiScript.Parse(line).ShouldBeOk();
        script.Directives[0].ShouldBeOfType<ScpiScriptDirective.Assert>().Pattern.ShouldBe(pattern);
    }

    [Theory]
    [InlineData("!echo hello world", "hello world")]
    [InlineData("!echo step #1 done", "step #1 done")]
    public void Parse_reads_bang_echo_whole_line_as_text(string line, string text)
    {
        var script = ScpiScript.Parse(line).ShouldBeOk();
        script.Directives[0].ShouldBeOfType<ScpiScriptDirective.Echo>().Text.ShouldBe(text);
    }

    [Fact]
    public void Parse_skips_bang_hash_comments_and_blank_lines_keeping_source_line_numbers()
    {
        var script = ScpiScript.Parse("!# header\n*RST\n\n!# trailing\n*IDN?").ShouldBeOk();
        script.Directives.Length.ShouldBe(2);
        var write = script.Directives[0].ShouldBeOfType<ScpiScriptDirective.Write>();
        write.Text.ShouldBe("*RST");
        write.Line.ShouldBe(2);
        var query = script.Directives[1].ShouldBeOfType<ScpiScriptDirective.Query>();
        query.Text.ShouldBe("*IDN?");
        query.Line.ShouldBe(5);
    }

    [Theory]
    [InlineData("echo ON")]
    [InlineData("sleep 10")]
    [InlineData("assert ok")]
    [InlineData("SOUR:VOLT #HFF")]
    [InlineData("DATA #800001000AB")]
    [InlineData("# header")]
    public void Parse_sends_unprefixed_line_verbatim_as_write(string line)
    {
        var script = ScpiScript.Parse(line).ShouldBeOk();
        script
            .Directives.ShouldHaveSingleItem()
            .ShouldBeOfType<ScpiScriptDirective.Write>()
            .Text.ShouldBe(line);
    }

    [Fact]
    public void Parse_sends_query_with_hash_verbatim()
    {
        var script = ScpiScript.Parse("*IDN? # ask identity").ShouldBeOk();
        script
            .Directives.ShouldHaveSingleItem()
            .ShouldBeOfType<ScpiScriptDirective.Query>()
            .Text.ShouldBe("*IDN? # ask identity");
    }
}
