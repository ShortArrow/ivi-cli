using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Scpi;
using IviCli.Domain.Visa;
using IviCli.Plugin;

namespace IviCli.Plugin.Sample;

/// <summary>
/// Reference implementation showing how a third-party plugin DLL
/// publishes a custom <see cref="IIviBackend"/> to the host
/// (ADR 0013). Plugin authors mirror this shape: implement
/// <see cref="IIviPlugin"/> with a parameterless constructor, then
/// call <see cref="IPluginServices.AddBackend"/> in
/// <see cref="Register"/> for each backend the plugin contributes.
/// </summary>
public sealed class SampleAcmePlugin : IIviPlugin
{
    /// <inheritdoc/>
    public string Name => "acme-instruments";

    /// <inheritdoc/>
    public string Version => "1.0.0";

    /// <inheritdoc/>
    public int TargetApiVersion => HostApiVersion.Current;

    /// <inheritdoc/>
    public void Register(IPluginServices services)
    {
        services.AddBackend<SampleAcmeBackend>(resource =>
            resource is VisaResource.Tcpip t
            && t.LanDevice.StartsWith("acme", StringComparison.OrdinalIgnoreCase)
        );
    }
}

/// <summary>
/// Toy backend the sample plugin contributes — every query returns
/// the canned IDN string "ACME,X100,42,1.0" so the host's plugin
/// routing can be observed end-to-end.
/// </summary>
public sealed class SampleAcmeBackend : IIviBackend
{
    /// <inheritdoc/>
    public Task<Result<Unit, BackendError>> OpenAsync(Device device, CancellationToken ct) =>
        Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

    /// <inheritdoc/>
    public Task<Result<Unit, BackendError>> CloseAsync(Device device, CancellationToken ct) =>
        Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

    /// <inheritdoc/>
    public Task<Result<Unit, BackendError>> WriteAsync(
        Device device,
        ScpiCommand command,
        CancellationToken ct
    ) => Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

    /// <inheritdoc/>
    public Task<Result<string, BackendError>> QueryAsync(
        Device device,
        ScpiQuery query,
        CancellationToken ct
    ) => Task.FromResult(Result.Success<string, BackendError>("ACME,X100,42,1.0"));

    /// <inheritdoc/>
    public Task<Result<string, BackendError>> ReadAsync(Device device, CancellationToken ct) =>
        Task.FromResult(Result.Success<string, BackendError>("ACME,X100,42,1.0"));

    /// <inheritdoc/>
    public Task<Result<Unit, BackendError>> TriggerAsync(Device device, CancellationToken ct) =>
        Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));

#pragma warning disable CS1998
    /// <inheritdoc/>
    public async IAsyncEnumerable<ServiceRequest> ServiceRequestStream(
        Device device,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct
    )
    {
        yield break;
    }
#pragma warning restore CS1998
}
