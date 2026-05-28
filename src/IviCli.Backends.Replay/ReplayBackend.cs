using IviCli.Application.Backends;
using IviCli.Domain;
using IviCli.Domain.Devices;
using IviCli.Domain.Mock;
using IviCli.Domain.Scpi;

namespace IviCli.Backends.Replay;

/// <summary>
/// Deterministic playback <see cref="IIviBackend"/> per ADR 0028.
/// Differs from <c>FakeBackend</c> in that it consults exactly one
/// <see cref="MockScenario"/> and rejects any SCPI that does not match
/// a recorded <see cref="MockScene"/>. No DSL fallback, no synthetic
/// <c>*IDN?</c>, no programmatic state. Designed for "play a recording
/// from `mock scenario record` exactly as captured" use cases.
/// </summary>
/// <remarks>
/// Activation flows through <c>IVICLI_REPLAY=&lt;scenario-name&gt;</c>
/// detected in the CLI composition root; the registered scenario is
/// resolved at startup via <c>IScenarioStore.LoadAsync</c> and stamped
/// into this backend as the immutable playback source.
/// </remarks>
public sealed class ReplayBackend : IIviBackend
{
    private readonly MockScenario _scenario;

    /// <summary>Creates a backend bound to the supplied scenario.</summary>
    public ReplayBackend(MockScenario scenario)
    {
        _scenario = scenario;
    }

    /// <summary>The scenario this backend is playing back.</summary>
    public MockScenario Scenario => _scenario;

    /// <inheritdoc/>
    public Task<Result<Unit, BackendError>> OpenAsync(Device device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));
    }

    /// <inheritdoc/>
    public Task<Result<Unit, BackendError>> CloseAsync(Device device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Result.Success<Unit, BackendError>(Unit.Value));
    }

    /// <inheritdoc/>
    public Task<Result<Unit, BackendError>> WriteAsync(
        Device device,
        ScpiCommand command,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var scene = _scenario.FindByMatch(command.Value);
        if (scene is null)
        {
            return Task.FromResult(Fail(command.Value));
        }
        return Task.FromResult(ApplyForWrite(scene, command.Value));
    }

    /// <inheritdoc/>
    public Task<Result<string, BackendError>> QueryAsync(
        Device device,
        ScpiQuery query,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var scene = _scenario.FindByMatch(query.Value);
        if (scene is null)
        {
            return Task.FromResult(FailString(query.Value));
        }
        return Task.FromResult(ApplyForQuery(scene, query.Value));
    }

    /// <inheritdoc/>
    public Task<Result<string, BackendError>> ReadAsync(Device device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Pure replay has no concept of out-of-band reads — the recording
        // tracks query/write pairs only. ReadAsync therefore always misses.
        return Task.FromResult(FailString("(bare read)"));
    }

    private static Result<Unit, BackendError> ApplyForWrite(MockScene scene, string scpi) =>
        scene.Action switch
        {
            SceneAction.Ack => Result.Success<Unit, BackendError>(Unit.Value),
            SceneAction.Respond => Result.Failure<Unit, BackendError>(
                new ReplayActionMismatch(scpi, "scene returns a Respond for a Write")
            ),
            SceneAction.Fail f => Result.Failure<Unit, BackendError>(
                new ReplayCannedFailure(f.Variant, f.Detail)
            ),
            _ => Result.Failure<Unit, BackendError>(
                new ReplayActionMismatch(scpi, "unknown action")
            ),
        };

    private static Result<string, BackendError> ApplyForQuery(MockScene scene, string scpi) =>
        scene.Action switch
        {
            SceneAction.Respond r => Result.Success<string, BackendError>(r.Text),
            SceneAction.Ack => Result.Failure<string, BackendError>(
                new ReplayActionMismatch(scpi, "scene returns an Ack for a Query")
            ),
            SceneAction.Fail f => Result.Failure<string, BackendError>(
                new ReplayCannedFailure(f.Variant, f.Detail)
            ),
            _ => Result.Failure<string, BackendError>(
                new ReplayActionMismatch(scpi, "unknown action")
            ),
        };

    /// <inheritdoc/>
    public Task<Result<Unit, BackendError>> TriggerAsync(Device device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(
            Result.Failure<Unit, BackendError>(
                new BackendOperationNotSupported(
                    "trigger",
                    device.Name,
                    "replay backend has no recorded trigger scenes"
                )
            )
        );
    }

    /// <inheritdoc/>
#pragma warning disable CS1998 // empty async iterator is intentional
    public async IAsyncEnumerable<ServiceRequest> ServiceRequestStream(
        Device device,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct
    )
    {
        // Pure replay has no SRQ semantics; the stream completes immediately.
        yield break;
    }
#pragma warning restore CS1998

    private static Result<Unit, BackendError> Fail(string scpi) =>
        Result.Failure<Unit, BackendError>(new ReplayMiss(scpi));

    private static Result<string, BackendError> FailString(string scpi) =>
        Result.Failure<string, BackendError>(new ReplayMiss(scpi));
}
