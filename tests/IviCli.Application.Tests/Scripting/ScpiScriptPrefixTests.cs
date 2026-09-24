using IviCli.Application.Scripting;
using IviCli.Domain;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Application.Tests.Scripting;

/// <summary>
/// 0.3.x reads both script forms: a directive or comment written with the
/// <c>!</c> prefix that 0.4.0 requires, and the unprefixed form 0.4.0
/// removes. The unprefixed form still works and is reported, line by line,
/// so a script can be moved before the upgrade breaks it.
/// </summary>
public sealed class ScpiScriptPrefixTests
{
    [Theory]
    [InlineData("!sleep 500")]
    [InlineData("!SLEEP 500")]
    public void A_prefixed_sleep_is_a_sleep_and_is_not_reported(string line)
    {
        var script = ScpiScript.Parse(line).ShouldBeOk();
        script
            .Directives[0]
            .ShouldBeOfType<ScpiScriptDirective.Sleep>()
            .Duration.ShouldBe(TimeSpan.FromMilliseconds(500));
        script.Deprecations.ShouldBeEmpty();
    }

    [Fact]
    public void A_prefixed_assert_keeps_a_hash_in_its_pattern()
    {
        var script = ScpiScript.Parse("!assert ^#15").ShouldBeOk();
        script.Directives[0].ShouldBeOfType<ScpiScriptDirective.Assert>().Pattern.ShouldBe("^#15");
        script.Deprecations.ShouldBeEmpty();
    }

    [Fact]
    public void A_prefixed_echo_keeps_its_text_whole()
    {
        var script = ScpiScript.Parse("!echo step #1 done").ShouldBeOk();
        script
            .Directives[0]
            .ShouldBeOfType<ScpiScriptDirective.Echo>()
            .Text.ShouldBe("step #1 done");
    }

    [Fact]
    public void A_prefixed_comment_is_skipped_and_is_not_reported()
    {
        var script = ScpiScript.Parse("!# bench check\n*IDN?").ShouldBeOk();
        script.Directives.Length.ShouldBe(1);
        script.Directives[0].Line.ShouldBe(2);
        script.Deprecations.ShouldBeEmpty();
    }

    [Fact]
    public void An_unknown_prefixed_directive_is_refused()
    {
        var error = ScpiScript.Parse("!wait 5").ShouldBeError();
        error.ShouldBeOfType<ScpiScriptInvalidDirective>().Line.ShouldBe(1);
    }

    [Theory]
    [InlineData("sleep 500", "!sleep 500")]
    [InlineData("assert ^OK", "!assert ^OK")]
    [InlineData("echo hi", "!echo hi")]
    public void An_unprefixed_directive_still_works_and_is_reported(string line, string replacement)
    {
        var script = ScpiScript.Parse("*RST\n" + line).ShouldBeOk();
        script.Directives.Length.ShouldBe(2);
        var reported = script.Deprecations.ShouldHaveSingleItem();
        reported.Line.ShouldBe(2);
        reported.Written.ShouldBe(line);
        reported.Replacement.ShouldBe(replacement);
    }

    [Fact]
    public void A_whole_line_hash_comment_is_still_skipped_and_is_reported()
    {
        var script = ScpiScript.Parse("# bench check\n*IDN?").ShouldBeOk();
        script.Directives.Length.ShouldBe(1);
        var reported = script.Deprecations.ShouldHaveSingleItem();
        reported.Line.ShouldBe(1);
        reported.Written.ShouldBe("# bench check");
        reported.Replacement.ShouldBe("!# bench check");
    }

    [Fact]
    public void A_trailing_hash_comment_is_still_stripped_and_is_reported()
    {
        var script = ScpiScript.Parse("SOUR:VOLT #HFF").ShouldBeOk();
        script.Directives[0].ShouldBeOfType<ScpiScriptDirective.Write>().Text.ShouldBe("SOUR:VOLT");
        var reported = script.Deprecations.ShouldHaveSingleItem();
        reported.Line.ShouldBe(1);
        reported.Written.ShouldBe("SOUR:VOLT #HFF");
        reported.Message.ShouldContain("0.4.0");
    }

    [Fact]
    public void A_script_in_the_new_form_reports_nothing()
    {
        var script = ScpiScript
            .Parse("!# check\n*RST\n!sleep 10\nMEAS:VOLT?\n!assert ^5\n!echo ok")
            .ShouldBeOk();
        script.Directives.Length.ShouldBe(5);
        script.Deprecations.ShouldBeEmpty();
    }
}
