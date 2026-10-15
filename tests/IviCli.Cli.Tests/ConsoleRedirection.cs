namespace IviCli.Cli.Tests;

/// <summary>
/// Tests that redirect <see cref="Console.Out"/> or <see cref="Console.Error"/>
/// run one at a time, so one test's redirection never swallows another's
/// output.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleRedirection
{
    /// <summary>The collection's name.</summary>
    public const string Name = "Console";
}
