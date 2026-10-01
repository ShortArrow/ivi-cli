using System.Text.Json;
using Shouldly;

namespace IviCli.Cli.Tests;

/// <summary>
/// Runtime settings the shipped <c>ivicli</c> carries in its
/// runtimeconfig.json, which every publish (self-contained archives, the
/// dotnet tool, packages) inherits from the project.
/// </summary>
public sealed class RuntimeConfigTests
{
    /// <summary>
    /// A Linux machine without ICU, such as a slim container image, must
    /// still run ivicli: with globalization on, the runtime aborts at start
    /// up when it finds no ICU library.
    /// </summary>
    [Fact]
    public void Globalization_runs_invariant_so_ICU_is_not_needed()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ivicli.runtimeconfig.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        var invariant = doc
            .RootElement.GetProperty("runtimeOptions")
            .GetProperty("configProperties")
            .TryGetProperty("System.Globalization.Invariant", out var value);

        invariant.ShouldBeTrue("ivicli.runtimeconfig.json sets no System.Globalization.Invariant");
        value.GetBoolean().ShouldBeTrue();
    }
}
