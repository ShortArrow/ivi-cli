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

    [Fact]
    public void Parse_classifies_write_when_line_lacks_question_mark()
    {
        var script = ScpiScript.Parse("*RST").ShouldBeOk();
        script.Directives.Length.ShouldBe(1);
        script.Directives[0].ShouldBeOfType<ScpiScriptDirective.Write>().Text.ShouldBe("*RST");
    }

    [Fact]
    public void Parse_ignores_blank_and_comment_lines()
    {
        var src = "\n# header\n*IDN?\n\n# trailing\n";
        var script = ScpiScript.Parse(src).ShouldBeOk();
        script.Directives.Length.ShouldBe(1);
        script.Directives[0].Line.ShouldBe(3);
    }

    [Fact]
    public void Parse_strips_trailing_comments()
    {
        var script = ScpiScript.Parse("*IDN? # ask identity").ShouldBeOk();
        var q = script.Directives[0].ShouldBeOfType<ScpiScriptDirective.Query>();
        q.Text.ShouldBe("*IDN?");
    }

    [Fact]
    public void Parse_recognises_sleep_directive()
    {
        var script = ScpiScript.Parse("sleep 250").ShouldBeOk();
        var s = script.Directives[0].ShouldBeOfType<ScpiScriptDirective.Sleep>();
        s.Duration.ShouldBe(TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public void Parse_rejects_negative_sleep()
    {
        var result = ScpiScript.Parse("sleep -1");
        result.ShouldBeOfType<Result<ScpiScript, ScpiScriptError>.Error>();
    }

    [Fact]
    public void Parse_recognises_assert_directive()
    {
        var script = ScpiScript.Parse("assert ^FAKE,.*$").ShouldBeOk();
        var a = script.Directives[0].ShouldBeOfType<ScpiScriptDirective.Assert>();
        a.Pattern.ShouldBe("^FAKE,.*$");
    }

    [Fact]
    public void Parse_recognises_echo_directive()
    {
        var script = ScpiScript.Parse("echo hello world").ShouldBeOk();
        var e = script.Directives[0].ShouldBeOfType<ScpiScriptDirective.Echo>();
        e.Text.ShouldBe("hello world");
    }

    [Fact]
    public void Parse_preserves_source_line_numbers()
    {
        var script = ScpiScript.Parse("# c1\n*RST\n*IDN?").ShouldBeOk();
        script.Directives[0].Line.ShouldBe(2);
        script.Directives[1].Line.ShouldBe(3);
    }
}
