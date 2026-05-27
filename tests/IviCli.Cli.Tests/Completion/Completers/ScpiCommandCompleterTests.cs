using IviCli.Cli.Completion.Completers;
using Shouldly;

namespace IviCli.Cli.Tests.Completion.Completers;

public sealed class ScpiCommandCompleterTests
{
    [Fact]
    public async Task CompleteAsync_returns_all_roots_when_prefix_empty()
    {
        var completer = new ScpiCommandCompleter();

        var candidates = await completer.CompleteAsync(string.Empty, default);

        candidates.Length.ShouldBeGreaterThan(20);
        candidates.ShouldContain("MEASure");
        candidates.ShouldContain("*IDN?");
    }

    [Fact]
    public async Task CompleteAsync_filters_case_insensitively_by_prefix()
    {
        var completer = new ScpiCommandCompleter();

        var candidates = await completer.CompleteAsync("se", default);

        candidates.ShouldContain("SENSe");
        candidates.ShouldNotContain("MEASure");
    }
}
