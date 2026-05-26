using System.CommandLine;
using IviCli.Cli.Completion;
using Shouldly;

namespace IviCli.Cli.Tests.Completion;

public sealed class CommandTreeWalkerTests
{
    private static RootCommand BuildSampleRoot()
    {
        var visa = new Command("visa", "VISA group.");
        visa.Subcommands.Add(new Command("scan", "Scan."));
        visa.Subcommands.Add(new Command("script", "Script."));
        visa.Subcommands.Add(new Command("status", "Status."));
        visa.Subcommands.Add(new Command("query", "Query."));
        visa.Options.Add(new Option<string?>("--device") { Description = "Device alias." });

        var hidden = new Command("__internal", "Hidden.") { Hidden = true };

        var root = new RootCommand("ivicli root.");
        root.Subcommands.Add(visa);
        root.Subcommands.Add(new Command("server", "Server group."));
        root.Subcommands.Add(hidden);
        return root;
    }

    [Fact]
    public void Tokenize_splits_on_whitespace_and_preserves_trailing_empty()
    {
        var tokens = CommandTreeWalker.Tokenize("ivicli visa ");
        tokens.Length.ShouldBe(3);
        tokens[0].ShouldBe("ivicli");
        tokens[1].ShouldBe("visa");
        tokens[2].ShouldBe(string.Empty);
    }

    [Fact]
    public void Tokenize_keeps_quoted_words_intact()
    {
        var tokens = CommandTreeWalker.Tokenize("ivicli visa query \"*IDN?\"");
        tokens.Length.ShouldBe(4);
        tokens[3].ShouldBe("*IDN?");
    }

    [Fact]
    public void Complete_lists_top_level_subcommands_when_prefix_empty()
    {
        var root = BuildSampleRoot();
        string[] tokens = [string.Empty];
        var candidates = CommandTreeWalker.Complete(root, tokens);
        candidates.ShouldContain("visa");
        candidates.ShouldContain("server");
    }

    [Fact]
    public void Complete_descends_into_named_subcommand()
    {
        var root = BuildSampleRoot();
        string[] tokens = ["visa", "s"];
        var candidates = CommandTreeWalker.Complete(root, tokens);
        string[] expected = ["scan", "script", "status"];
        candidates.ShouldBe(expected);
    }

    [Fact]
    public void Complete_excludes_hidden_subcommands()
    {
        var root = BuildSampleRoot();
        string[] tokens = [string.Empty];
        var candidates = CommandTreeWalker.Complete(root, tokens);
        candidates.ShouldNotContain("__internal");
    }

    [Fact]
    public void Complete_returns_option_names_when_prefix_starts_with_dash()
    {
        var root = BuildSampleRoot();
        string[] tokens = ["visa", "--"];
        var candidates = CommandTreeWalker.Complete(root, tokens);
        candidates.ShouldContain("--device");
        candidates.ShouldNotContain("scan");
    }

    [Fact]
    public void Complete_returns_empty_when_no_candidates_match_prefix()
    {
        var root = BuildSampleRoot();
        string[] tokens = ["visa", "zzz"];
        var candidates = CommandTreeWalker.Complete(root, tokens);
        candidates.ShouldBeEmpty();
    }
}
