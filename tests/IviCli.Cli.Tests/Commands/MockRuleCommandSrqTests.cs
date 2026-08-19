using System.Collections.Immutable;
using IviCli.Application.Mock;
using IviCli.Cli.Commands;
using IviCli.Domain;
using IviCli.Domain.Mock;
using IviCli.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace IviCli.Cli.Tests.Commands;

/// <summary>
/// <c>mock rule add --srq</c> is where an operator says which
/// status byte the mock reports when the rule fires, so the option is
/// exercised the way an operator uses it: parse a command line, run it,
/// and read the rule back out of the scenario it wrote.
/// </summary>
public sealed class MockRuleCommandSrqTests
{
    private static readonly ScenarioName Demo = ScenarioName.From("demo").ShouldBeOk();

    [Theory]
    [InlineData("0x60")]
    [InlineData("0X60")]
    [InlineData("96")]
    public async Task Srq_option_accepts_decimal_and_hex(string raw)
    {
        var store = Seeded();

        var exitCode = await RunAsync(
            store,
            "add",
            "demo",
            "--match",
            "*OPC",
            "--ack",
            "--srq",
            raw
        );

        exitCode.ShouldBe(0);
        (await RuleOfAsync(store)).Srq.ShouldBe<byte?>(0x60);
    }

    [Fact]
    public async Task Without_the_option_the_rule_carries_no_srq()
    {
        var store = Seeded();

        var exitCode = await RunAsync(store, "add", "demo", "--match", "*OPC", "--ack");

        exitCode.ShouldBe(0);
        (await RuleOfAsync(store)).Srq.ShouldBeNull();
    }

    [Theory]
    [InlineData("300")]
    [InlineData("abc")]
    public async Task Srq_out_of_range_is_refused_and_nothing_is_written(string raw)
    {
        var store = Seeded();

        var exitCode = await RunAsync(
            store,
            "add",
            "demo",
            "--match",
            "*OPC",
            "--ack",
            "--srq",
            raw
        );

        exitCode.ShouldBe(ExitCodeMapper.UsageError);
        var scenario = (await store.LoadAsync(Demo, CancellationToken.None)).ShouldBeOk();
        scenario.Scenes[0].Rules.ShouldBeEmpty();
    }

    [Fact]
    public async Task Show_renders_the_status_byte_a_rule_raises()
    {
        var store = new FakeScenarioStore([
            MockScenario.SingleScene(
                Demo,
                idnDefault: null,
                rules: ImmutableArray.Create(new MockRule("*OPC", new RuleAction.Ack(), Srq: 0x60))
            ),
        ]);
        var services = new ServiceCollection()
            .AddSingleton<IScenarioStore>(store)
            .AddSingleton<ShowScenarioQueryHandler>()
            .AddLogging()
            .BuildServiceProvider();

        var original = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            var exitCode = await MockScenarioCommand
                .Build(services)
                .Parse(["show", "demo"])
                .InvokeAsync(cancellationToken: CancellationToken.None);
            exitCode.ShouldBe(0);
        }
        finally
        {
            Console.SetOut(original);
        }

        captured.ToString().ShouldContain("1. *OPC -> ack (srq 0x60)");
    }

    private static async Task<int> RunAsync(FakeScenarioStore store, params string[] args)
    {
        var services = new ServiceCollection()
            .AddSingleton<IScenarioStore>(store)
            .AddSingleton<AddRuleCommandHandler>()
            .AddSingleton<RemoveRuleCommandHandler>()
            .AddLogging()
            .BuildServiceProvider();

        return await MockRuleCommand
            .Build(services)
            .Parse(args)
            .InvokeAsync(cancellationToken: CancellationToken.None);
    }

    private static async Task<MockRule> RuleOfAsync(FakeScenarioStore store)
    {
        var scenario = (await store.LoadAsync(Demo, CancellationToken.None)).ShouldBeOk();
        return scenario.Scenes[0].Rules.ShouldHaveSingleItem();
    }

    private static FakeScenarioStore Seeded() => new([MockScenario.Empty(Demo)]);
}
