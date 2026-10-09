using System.Diagnostics;
using System.Runtime.CompilerServices;
using IviCli.Application.Backends;
using IviCli.Cli;
using IviCli.Domain;
using IviCli.Domain.Configuration;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;
using IviCli.TestKit;
using Shouldly;

namespace IviCli.Cli.Tests;

/// <summary>
/// The layers the CLI puts around the routing backend factory bound every
/// operation by the device's timeout, whether or not the pool is enabled
/// and whether the backend is built in or comes from a plugin.
/// </summary>
public sealed class BackendLayersTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    private static readonly Device Dut = new(
        DeviceName.From("dut").ShouldBeOk(),
        VisaResource.Parse("TCPIP0::127.0.0.1::5025::SOCKET").ShouldBeOk(),
        Timeout.FromMilliseconds(200).ShouldBeOk()
    );

    public static TheoryData<bool> PoolEnabled => new() { false, true };

    [Theory]
    [MemberData(nameof(PoolEnabled))]
    public async Task A_query_to_a_silent_built_in_backend_times_out(bool poolEnabled)
    {
        var factory = BackendLayers.Compose(
            new FakeBackendFactory(new SilentBackend()),
            plugins: null,
            Pool(poolEnabled),
            TimeProvider.System,
            poolLogger: null
        );

        (await QueryOnce(factory)).Err.ShouldBeOfType<TransportTimeout>();
    }

    [Fact]
    public async Task A_query_to_a_silent_plugin_backend_times_out()
    {
        var factory = BackendLayers.Compose(
            new FakeBackendFactory(new FakeAnsweringBackend()),
            plugins: _ => new FakeBackendFactory(new SilentBackend()),
            Pool(enabled: true),
            TimeProvider.System,
            poolLogger: null
        );

        (await QueryOnce(factory)).Err.ShouldBeOfType<TransportTimeout>();
    }

    private static PoolConfig Pool(bool enabled) =>
        PoolConfig.From(enabled, TimeSpan.FromSeconds(60), 16).ShouldBeOk();

    private static async Task<Result<string, BackendError>.Error> QueryOnce(IBackendFactory factory)
    {
        var backend = factory.CreateFor(Dut).ShouldBeOk();
        (await backend.OpenAsync(Dut, CancellationToken.None).WaitAsync(Guard)).ShouldBeOk();
        var result = await backend
            .QueryAsync(Dut, ScpiQuery.From("*IDN?").ShouldBeOk(), CancellationToken.None)
            .WaitAsync(Guard);
        await backend.CloseAsync(Dut, CancellationToken.None);
        return result.ShouldBeOfType<Result<string, BackendError>.Error>();
    }

    /// <summary>Opens, then never answers until the operation is cancelled.</summary>
    private sealed class SilentBackend : FakeAnsweringBackend
    {
        public override async Task<Result<string, BackendError>> QueryAsync(
            Device device,
            ScpiQuery query,
            CancellationToken ct
        )
        {
            await Task.Delay(System.Threading.Timeout.Infinite, ct);
            throw new UnreachableException();
        }
    }

    /// <summary>Opens, and answers every query at once.</summary>
    private class FakeAnsweringBackend : IIviBackend
    {
        public Task<Result<Unit, BackendError>> OpenAsync(Device device, CancellationToken ct) =>
            Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

        public Task<Result<Unit, BackendError>> CloseAsync(Device device, CancellationToken ct) =>
            Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

        public Task<Result<Unit, BackendError>> WriteAsync(
            Device device,
            ScpiCommand command,
            CancellationToken ct
        ) => Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

        public virtual Task<Result<string, BackendError>> QueryAsync(
            Device device,
            ScpiQuery query,
            CancellationToken ct
        ) => Task.FromResult(Result.Success<string, BackendError>("ok"));

        public Task<Result<string, BackendError>> ReadAsync(Device device, CancellationToken ct) =>
            Task.FromResult(Result.Success<string, BackendError>("ok"));

        public Task<Result<Unit, BackendError>> TriggerAsync(Device device, CancellationToken ct) =>
            Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

        public async IAsyncEnumerable<ServiceRequest> ServiceRequestStream(
            Device device,
            [EnumeratorCancellation] CancellationToken ct
        )
        {
            await Task.Yield();
            yield break;
        }
    }
}
