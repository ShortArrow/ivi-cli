using System.CommandLine;
using IviCli.Application.Configuration;
using IviCli.Application.Servers;
using IviCli.Cli.Commands;
using IviCli.Domain.Configuration;
using IviCli.Domain.Servers;
using IviCli.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace IviCli.Cli.Tests.Commands;

/// <summary>
/// <c>server add</c> is the only place an operator learns which server
/// types exist, so its help text and its per-type port defaults are pinned
/// here for every type the gateway can start.
/// </summary>
public sealed class ServerCommandAddTests
{
    [Theory]
    [InlineData("socket")]
    [InlineData("hislip")]
    [InlineData("vxi11")]
    [InlineData("usbip")]
    public void The_type_option_names_every_startable_server_type(string type)
    {
        var typeOption = ServerCommand
            .Build(Services(new FakeConfigStore(ConfigDocument.Empty)))
            .Subcommands.Single(c => c.Name == "add")
            .Options.Single(o => o.Name == "--type");

        typeOption.Description.ShouldNotBeNull().ShouldContain(type);
    }

    [Theory]
    [InlineData("socket", 5025)]
    [InlineData("hislip", 4880)]
    [InlineData("usbip", 3240)]
    public async Task Omitting_the_port_uses_the_type_default(string type, int expectedPort)
    {
        var store = new FakeConfigStore(ConfigDocument.Empty);

        var exitCode = await ServerCommand
            .Build(Services(store))
            .Parse(["add", "srv", "--type", type])
            .InvokeAsync(CancellationToken.None);

        exitCode.ShouldBe(0);
        var config = (await store.LoadAsync(CancellationToken.None)).ShouldBeOk();
        config.Servers.ShouldHaveSingleItem().Port.Value.ShouldBe(expectedPort);
    }

    private static ServiceProvider Services(FakeConfigStore store) =>
        new ServiceCollection()
            .AddSingleton<IConfigStore>(store)
            .AddSingleton<AddServerCommandHandler>()
            .AddSingleton<RemoveServerCommandHandler>()
            .AddSingleton<ListServersQueryHandler>()
            .AddLogging()
            .BuildServiceProvider();
}
