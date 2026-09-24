using IviCli.Cli.Commands;
using Shouldly;

namespace IviCli.Cli.Tests.Commands;

/// <summary>
/// Running or recording a script written in the form 0.4.0 removes prints
/// one warning per such line, so the author sees what to change while the
/// script still works.
/// </summary>
public sealed class ScriptDeprecationWarningsTests
{
    [Fact]
    public void Each_old_form_line_gets_one_warning()
    {
        var writer = new StringWriter();

        ScriptDeprecationWarnings.Write("# header\n*RST\nsleep 10\n*IDN?", writer);

        var lines = writer
            .ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(2);
        lines.ShouldAllBe(l => l.StartsWith("warning: line "));
        lines[1].ShouldContain("!sleep 10");
    }

    [Fact]
    public void A_script_in_the_new_form_prints_nothing()
    {
        var writer = new StringWriter();

        ScriptDeprecationWarnings.Write("!# header\n*RST\n!sleep 10\n*IDN?", writer);

        writer.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void A_script_that_does_not_parse_prints_nothing_here()
    {
        var writer = new StringWriter();

        ScriptDeprecationWarnings.Write("!bogus", writer);

        writer.ToString().ShouldBeEmpty();
    }
}
