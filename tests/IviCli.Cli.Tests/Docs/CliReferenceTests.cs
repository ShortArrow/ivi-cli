using System.CommandLine;
using System.CommandLine.Help;
using System.Globalization;
using System.Text;
using IviCli.Cli.Commands;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace IviCli.Cli.Tests.Docs;

/// <summary>
/// Keeps <c>docs/cli-reference.md</c> equal to the command tree, so that a
/// change to a command, an argument or an option shows as a change to that
/// document. Set <c>IVICLI_UPDATE_CLI_REFERENCE=1</c> to rewrite the file
/// from the tree instead of comparing.
/// </summary>
public sealed class CliReferenceTests
{
    private const string UpdateVariable = "IVICLI_UPDATE_CLI_REFERENCE";
    private const string RelativePath = "docs/cli-reference.md";

    [Fact]
    public void Cli_reference_matches_the_command_tree()
    {
        var root = CommandTree.Build(new ServiceCollection().BuildServiceProvider());
        var rendered = Render(root);
        var path = Path.Combine(RepositoryRoot(), RelativePath);

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(path, rendered, new UTF8Encoding(false));
        }

        File.Exists(path)
            .ShouldBeTrue($"{RelativePath} is missing; run the tests with {UpdateVariable}=1.");
        File.ReadAllText(path)
            .ReplaceLineEndings("\n")
            .ShouldBe(
                rendered,
                $"{RelativePath} differs from the command tree; run the tests with {UpdateVariable}=1 and commit the file."
            );
    }

    private static string Render(RootCommand root)
    {
        var builder = new StringBuilder();
        builder.Append("# Command reference\n\n");
        builder.Append(
            "Every `ivicli` command, argument and option, generated from the command tree by\n"
                + "`CliReferenceTests`. Edit the descriptions in the command's source and run the\n"
                + $"tests with `{UpdateVariable}=1` to rewrite this page.\n"
        );
        RenderCommand(builder, root, "ivicli");
        return builder.ToString();
    }

    private static void RenderCommand(StringBuilder builder, Command command, string path)
    {
        if (command.Hidden)
        {
            return;
        }

        builder.Append(CultureInfo.InvariantCulture, $"\n## `{path}`\n\n");
        if (!string.IsNullOrWhiteSpace(command.Description))
        {
            builder.Append(Cell(command.Description)).Append('\n');
        }
        if (command.Aliases.Count > 0)
        {
            builder.Append(
                CultureInfo.InvariantCulture,
                $"\nAliases: {string.Join(", ", command.Aliases.Order(StringComparer.Ordinal).Select(a => $"`{a}`"))}\n"
            );
        }

        var arguments = command.Arguments.Where(a => !a.Hidden).ToList();
        if (arguments.Count > 0)
        {
            builder.Append("\n| Argument | Arity | Description |\n| --- | --- | --- |\n");
            foreach (var a in arguments)
            {
                builder.Append(
                    CultureInfo.InvariantCulture,
                    $"| `{a.Name}` | {Arity(a.Arity)} | {Cell(a.Description)} |\n"
                );
            }
        }

        var options = command
            .Options.Where(o => !o.Hidden && o is not HelpOption && o is not VersionOption)
            .ToList();
        if (options.Count > 0)
        {
            builder.Append(
                "\n| Option | Aliases | Required | Arity | Description |\n| --- | --- | --- | --- | --- |\n"
            );
            foreach (var o in options)
            {
                var aliases = string.Join(
                    ", ",
                    o.Aliases.Order(StringComparer.Ordinal).Select(a => $"`{a}`")
                );
                var scope = o.Recursive ? " (applies to every subcommand)" : "";
                builder.Append(
                    CultureInfo.InvariantCulture,
                    $"| `{o.Name}` | {aliases} | {(o.Required ? "yes" : "no")} | {Arity(o.Arity)} | {Cell(o.Description)}{scope} |\n"
                );
            }
        }

        foreach (var sub in command.Subcommands)
        {
            RenderCommand(builder, sub, $"{path} {sub.Name}");
        }
    }

    private static string Arity(ArgumentArity arity) =>
        arity.MaximumNumberOfValues == int.MaxValue ? $"{arity.MinimumNumberOfValues}..*"
        : arity.MinimumNumberOfValues == arity.MaximumNumberOfValues
            ? arity.MinimumNumberOfValues.ToString(CultureInfo.InvariantCulture)
        : $"{arity.MinimumNumberOfValues}..{arity.MaximumNumberOfValues}";

    private static string Cell(string? text) =>
        (text ?? "").ReplaceLineEndings(" ").Replace("|", "\\|", StringComparison.Ordinal).Trim();

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ivi-cli.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName
            ?? throw new InvalidOperationException("ivi-cli.slnx not found above the test output.");
    }
}
