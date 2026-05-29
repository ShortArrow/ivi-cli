using IviCli.Cli.Audit;
using Shouldly;

namespace IviCli.Cli.Tests.Audit;

public sealed class CliAuditSubjectTests
{
    [Fact]
    public void Get_returns_cli_prefixed_environment_user()
    {
        var subject = new CliAuditSubject().Get();
        subject.ShouldBe($"cli/{Environment.UserName}");
    }

    [Fact]
    public void Get_is_stable_across_calls()
    {
        var sut = new CliAuditSubject();
        sut.Get().ShouldBe(sut.Get());
    }
}
