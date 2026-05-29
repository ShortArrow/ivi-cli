using System.Collections.Immutable;
using IviCli.Application.Audit;
using IviCli.Application.Servers;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Servers;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Application.Tests.Servers;

/// <summary>
/// Locks in <see cref="ServerLifecycle"/> emission from
/// <see cref="StartServerCommandHandler"/> (ADR 0043 Batch U).
/// </summary>
public sealed class StartServerLifecycleAuditTests
{
    private const string ServerNameValue = "gw1";
    private static readonly string[] StartThenStop = ["start", "stop"];
    private static readonly string[] StartThenCrashed = ["start", "crashed"];

    [Fact]
    public async Task Normal_run_emits_start_then_stop()
    {
        var fixture = await RunAsync(gateway => gateway.ReturnSuccess());

        fixture.AuditActions().ShouldBe(StartThenStop);
        fixture.Audit.Events.OfType<ServerLifecycle>().All(e => e.Subject == "test").ShouldBeTrue();
    }

    [Fact]
    public async Task Failed_run_emits_start_then_crashed()
    {
        var fixture = await RunAsync(gateway =>
            gateway.ReturnError(new UnsupportedServerType(ServerType.HiSlip))
        );

        fixture.AuditActions().ShouldBe(StartThenCrashed);
    }

    [Fact]
    public async Task Cancelled_run_emits_start_then_stop()
    {
        // Fake gateway acknowledges cancellation by returning Success — the
        // contract treats cooperative cancellation as a clean stop, not a
        // crash. Lifecycle audit must agree.
        var fixture = await RunAsync(gateway =>
            gateway.OnRun(async ct =>
            {
                await Task.Yield();
                ct.ThrowIfCancellationRequested();
                return Result.Success<Unit, GatewayServerError>(Unit.Value);
            })
        );

        fixture.AuditActions().ShouldBe(StartThenStop);
    }

    private static async Task<RunFixture> RunAsync(Action<FakeGateway> configureGateway)
    {
        var audit = new FakeAuditLog();
        var subject = new FakeAuditSubject("test");
        var gateway = new FakeGateway();
        configureGateway(gateway);

        var server = new Server(
            ServerName.From(ServerNameValue).ShouldBeOk(),
            ServerType.HiSlip,
            IpAddress.From("127.0.0.1").ShouldBeOk(),
            Port.From(4880).ShouldBeOk()
        );
        var config = ConfigDocument.Empty.AddServer(server).ShouldBeOk();
        var configStore = new FakeConfigStore(config);
        var registry = new FakeProcessRegistry();
        var factory = new FakeGatewayFactory(gateway);

        var handler = new StartServerCommandHandler(
            configStore,
            factory,
            registry,
            time: TimeProvider.System,
            audit: audit,
            subject: subject
        );

        await handler.HandleAsync(new StartServerCommand(ServerNameValue), default);
        return new RunFixture(audit);
    }

    private sealed record RunFixture(FakeAuditLog Audit)
    {
        public string[] AuditActions() =>
            Audit.Events.OfType<ServerLifecycle>().Select(e => e.Action).ToArray();
    }

    private sealed class FakeGateway : IGatewayServer
    {
        private Func<CancellationToken, Task<Result<Unit, GatewayServerError>>> _impl = _ =>
            Task.FromResult(Result.Success<Unit, GatewayServerError>(Unit.Value));

        public ServerType SupportedType => ServerType.HiSlip;

        public void ReturnSuccess() =>
            _impl = _ => Task.FromResult(Result.Success<Unit, GatewayServerError>(Unit.Value));

        public void ReturnError(GatewayServerError error) =>
            _impl = _ => Task.FromResult(Result.Failure<Unit, GatewayServerError>(error));

        public void OnRun(Func<CancellationToken, Task<Result<Unit, GatewayServerError>>> impl) =>
            _impl = impl;

        public Task<Result<Unit, GatewayServerError>> RunAsync(
            Server server,
            ConfigDocument config,
            CancellationToken ct
        ) => _impl(ct);
    }

    private sealed class FakeGatewayFactory : IGatewayServerFactory
    {
        private readonly IGatewayServer _gateway;

        public FakeGatewayFactory(IGatewayServer gateway) => _gateway = gateway;

        public Result<IGatewayServer, GatewayServerError> CreateFor(ServerType type) =>
            Result.Success<IGatewayServer, GatewayServerError>(_gateway);
    }

    private sealed class FakeProcessRegistry : IServerProcessRegistry
    {
        public Task<Result<Unit, ServerProcessRegistryError>> WriteAsync(
            ServerName name,
            int processId,
            DateTimeOffset startedAt,
            CancellationToken ct
        ) => Task.FromResult(Result.Success<Unit, ServerProcessRegistryError>(Unit.Value));

        public Task<Result<ServerProcessEntry?, ServerProcessRegistryError>> ReadAsync(
            ServerName name,
            CancellationToken ct
        ) => Task.FromResult(Result.Success<ServerProcessEntry?, ServerProcessRegistryError>(null));

        public Task<Result<Unit, ServerProcessRegistryError>> DeleteAsync(
            ServerName name,
            CancellationToken ct
        ) => Task.FromResult(Result.Success<Unit, ServerProcessRegistryError>(Unit.Value));

        public Task<
            Result<ImmutableArray<ServerProcessEntry>, ServerProcessRegistryError>
        > ListAsync(CancellationToken ct) =>
            Task.FromResult(
                Result.Success<ImmutableArray<ServerProcessEntry>, ServerProcessRegistryError>(
                    ImmutableArray<ServerProcessEntry>.Empty
                )
            );
    }
}
