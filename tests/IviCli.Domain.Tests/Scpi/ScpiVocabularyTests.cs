using IviCli.Domain.Scpi;
using Shouldly;

namespace IviCli.Domain.Tests.Scpi;

public sealed class ScpiVocabularyTests
{
    [Theory]
    [InlineData("*IDN?")]
    [InlineData("*idn?")]
    [InlineData("*RST")]
    [InlineData("*CLS")]
    [InlineData("*wai")]
    public void IsKnownRoot_recognises_IEEE_488_2_common_commands(string mnemonic)
    {
        ScpiVocabulary.IsKnownRoot(mnemonic).ShouldBeTrue();
    }

    [Theory]
    [InlineData("MEAS:VOLT:DC?", "MEAS")]
    [InlineData("MEASure:VOLT:DC?", "MEASure")]
    [InlineData("SENSe:CURR?", "SENSe")]
    [InlineData(":SYST:ERR?", "SYST with leading colon")]
    [InlineData("OUTP ON", "OUTP with parameter")]
    [InlineData("conf:volt:dc", "lowercase")]
    public void IsKnownRoot_recognises_SCPI_core_root_nodes(string mnemonic, string _)
    {
        ScpiVocabulary.IsKnownRoot(mnemonic).ShouldBeTrue();
    }

    [Theory]
    [InlineData("NOTACOMMAND")]
    [InlineData("MEASURX:VOLT?")]
    [InlineData("BOGUS:STUFF")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsKnownRoot_rejects_unknown_or_blank_input(string? mnemonic)
    {
        ScpiVocabulary.IsKnownRoot(mnemonic).ShouldBeFalse();
    }

    [Fact]
    public void RootsStartingWith_empty_prefix_returns_full_vocabulary_sorted()
    {
        var all = ScpiVocabulary.RootsStartingWith("");
        all.ShouldBe(ScpiVocabulary.AllRoots);
        all.Length.ShouldBeGreaterThan(20);
        var asArray = all.ToArray();
        asArray.OrderBy(x => x, StringComparer.Ordinal).ShouldBe(asArray);
    }

    [Fact]
    public void RootsStartingWith_prefix_matches_long_form_case_insensitively()
    {
        var results = ScpiVocabulary.RootsStartingWith("SE");
        results.ShouldContain("SENSe");

        var lower = ScpiVocabulary.RootsStartingWith("me");
        lower.ShouldContain("MEASure");
    }

    [Fact]
    public void AllRoots_includes_both_common_commands_and_core_roots()
    {
        ScpiVocabulary.AllRoots.ShouldContain("*IDN?");
        ScpiVocabulary.AllRoots.ShouldContain("MEASure");
        ScpiVocabulary.AllRoots.ShouldContain("SENSe");
        ScpiVocabulary.AllRoots.ShouldContain("SYSTem");
    }
}
