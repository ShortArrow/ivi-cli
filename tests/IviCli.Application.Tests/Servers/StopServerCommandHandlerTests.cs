using System.Collections.Immutable;
using IviCli.Application.Configuration;
using IviCli.Application.Servers;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Servers;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Application.Tests.Servers;

public class StopServerCommandHandlerTests
{
    private static (StopServerCommandHandler Handler, IServerProcessRegistry Registry) Build(
        params Server[] servers
    )
    {
        var doc = ConfigDocument.Empty;
        foreach (var s in servers)
        {
            doc = doc.AddServer(s).ShouldBeOk();
        }
        var config = new FakeConfigStore(doc);
        var registry = new FakeServerProcessRegistry();
        return (new StopServerCommandHandler(config, registry), registry);
    }

    private static Server MakeServer(string name) =>
        new(
            ServerName.From(name).ShouldBeOk(),
            ServerType.Socket,
            IpAddress.From("127.0.0.1").ShouldBeOk(),
            Port.From(5025).ShouldBeOk()
        );

    [Fact]
    public async Task HandleAsync_returns_entry_when_server_running()
    {
        var (handler, registry) = Build(MakeServer("sock1"));
        await registry.WriteAsync(
            ServerName.From("sock1").ShouldBeOk(),
            1234,
            DateTimeOffset.UtcNow,
            default
        );

        var result = await handler.HandleAsync(new StopServerCommand("sock1"), default);
        var entry = result.ShouldBeOk();
        entry.ProcessId.ShouldBe(1234);
    }

    [Fact]
    public async Task HandleAsync_fails_with_NotRunning_when_no_pid_file()
    {
        var (handler, _) = Build(MakeServer("sock1"));
        var result = await handler.HandleAsync(new StopServerCommand("sock1"), default);
        result.ShouldBeOfType<Result<ServerProcessEntry, StopServerError>.Error>();
        (
            (Result<ServerProcessEntry, StopServerError>.Error)result
        ).Err.ShouldBeOfType<StopServerNotRunning>();
    }

    [Fact]
    public async Task HandleAsync_fails_with_Unknown_when_server_absent_from_config()
    {
        var (handler, _) = Build();
        var result = await handler.HandleAsync(new StopServerCommand("ghost"), default);
        result.ShouldBeOfType<Result<ServerProcessEntry, StopServerError>.Error>();
        (
            (Result<ServerProcessEntry, StopServerError>.Error)result
        ).Err.ShouldBeOfType<StopServerUnknown>();
    }

    [Fact]
    public async Task HandleAsync_fails_with_InvalidName_when_name_is_garbage()
    {
        var (handler, _) = Build();
        var result = await handler.HandleAsync(new StopServerCommand("INVALID NAME"), default);
        result.ShouldBeOfType<Result<ServerProcessEntry, StopServerError>.Error>();
        (
            (Result<ServerProcessEntry, StopServerError>.Error)result
        ).Err.ShouldBeOfType<StopServerInvalidName>();
    }

    [Fact]
    public async Task ClearEntryAsync_removes_the_registry_entry()
    {
        var (handler, registry) = Build(MakeServer("sock1"));
        var name = ServerName.From("sock1").ShouldBeOk();
        await registry.WriteAsync(name, 1234, DateTimeOffset.UtcNow, default);

        var clear = await handler.ClearEntryAsync(name, default);
        clear.ShouldBeOk();

        (await registry.ReadAsync(name, default)).ShouldBeOk().ShouldBeNull();
    }
}

/// <summary>In-memory <see cref="IServerProcessRegistry"/> for handler tests.</summary>
file sealed class FakeServerProcessRegistry : IServerProcessRegistry
{
    private readonly Dictionary<string, ServerProcessEntry> _entries = new();

    public Task<Result<Unit, ServerProcessRegistryError>> WriteAsync(
        ServerName name,
        int processId,
        DateTimeOffset startedAt,
        CancellationToken ct
    )
    {
        _entries[name.Value] = new ServerProcessEntry(name, processId, startedAt);
        return Task.FromResult(Result.Success<Unit, ServerProcessRegistryError>(Unit.Value));
    }

    public Task<Result<ServerProcessEntry?, ServerProcessRegistryError>> ReadAsync(
        ServerName name,
        CancellationToken ct
    ) =>
        Task.FromResult(
            Result.Success<ServerProcessEntry?, ServerProcessRegistryError>(
                _entries.TryGetValue(name.Value, out var e) ? e : null
            )
        );

    public Task<Result<Unit, ServerProcessRegistryError>> DeleteAsync(
        ServerName name,
        CancellationToken ct
    )
    {
        _entries.Remove(name.Value);
        return Task.FromResult(Result.Success<Unit, ServerProcessRegistryError>(Unit.Value));
    }

    public Task<Result<ImmutableArray<ServerProcessEntry>, ServerProcessRegistryError>> ListAsync(
        CancellationToken ct
    ) =>
        Task.FromResult(
            Result.Success<ImmutableArray<ServerProcessEntry>, ServerProcessRegistryError>(
                _entries
                    .Values.OrderBy(e => e.Name.Value, StringComparer.Ordinal)
                    .ToImmutableArray()
            )
        );
}
