using IviCli.Application.Logging;
using IviCli.Cli.Logging;
using IviCli.Domain;
using Shouldly;
using Xunit;

namespace IviCli.Cli.Tests.Logging;

/// <summary>
/// Pins that the CLI maps <see cref="LogSeverity"/> to MEL levels exactly as
/// the shared application extension does, so a newly added severity cannot
/// drift between the two.
/// </summary>
public sealed class SerilogConfigurationTests
{
    [Theory]
    [MemberData(nameof(AllSeverities))]
    public void Maps_each_severity_like_the_shared_extension(LogSeverity severity)
    {
        SerilogConfiguration
            .ToLogLevel(severity)
            .ShouldBe(IviErrorLoggerExtensions.ToLogLevel(severity));
    }

    public static TheoryData<LogSeverity> AllSeverities()
    {
        var data = new TheoryData<LogSeverity>();
        foreach (var severity in Enum.GetValues<LogSeverity>())
        {
            data.Add(severity);
        }
        return data;
    }
}
