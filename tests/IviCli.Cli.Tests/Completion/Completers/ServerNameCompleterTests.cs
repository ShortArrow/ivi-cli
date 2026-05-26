using IviCli.Cli.Completion.Completers;
using IviCli.Domain.Configuration;
using IviCli.Domain.Servers;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Cli.Tests.Completion.Completers;

public sealed class ServerNameCompleterTests
{
    private static IviCli.Domain.Servers.Server Srv(string name) =>
        new(
            ServerName.From(name).ShouldBeOk(),
            ServerType.Socket,
            IpAddress.From("127.0.0.1").ShouldBeOk(),
            Port.From(5025).ShouldBeOk()
        );

    private static FakeConfigStore StoreWith(params IviCli.Domain.Servers.Server[] servers)
    {
        var doc = ConfigDocument.Empty;
        foreach (var s in servers)
        {
            doc = doc.AddServer(s).ShouldBeOk();
        }
        return new FakeConfigStore(doc);
    }

    [Fact]
    public async Task CompleteAsync_returns_all_servers_when_prefix_empty()
    {
        var completer = new ServerNameCompleter(StoreWith(Srv("sock1"), Srv("hislip1")));
        var candidates = await completer.CompleteAsync(string.Empty, default);
        string[] expected = ["hislip1", "sock1"];
        candidates.ShouldBe(expected);
    }

    [Fact]
    public async Task CompleteAsync_filters_by_prefix()
    {
        var completer = new ServerNameCompleter(
            StoreWith(Srv("sock1"), Srv("hislip1"), Srv("sock2"))
        );
        var candidates = await completer.CompleteAsync("sock", default);
        string[] expected = ["sock1", "sock2"];
        candidates.ShouldBe(expected);
    }
}
