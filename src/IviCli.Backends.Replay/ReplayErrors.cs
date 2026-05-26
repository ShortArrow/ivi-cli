using IviCli.Application.Backends;
using IviCli.Domain;

namespace IviCli.Backends.Replay;

/// <summary>
/// Replay scenario does not contain a scene matching the requested SCPI.
/// The CLI maps this to a usage-error exit code so missing-scene
/// playback is a hard failure (the operator is expected to re-record
/// or extend the scenario).
/// </summary>
public sealed record ReplayMiss(string Scpi) : BackendError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "replay miss: no scene matches {Scpi}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Scpi };
}

/// <summary>
/// A scene matched but its action contradicts the operation type — for
/// example a <c>Respond</c> scene matched a <c>WriteAsync</c> call.
/// </summary>
public sealed record ReplayActionMismatch(string Scpi, string Reason) : BackendError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "replay action mismatch for {Scpi}: {Reason}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Scpi, Reason };
}

/// <summary>
/// The matched scene declared an explicit failure (<c>SceneAction.Fail</c>).
/// The variant string is preserved verbatim so log readers can correlate
/// it with the scenario file.
/// </summary>
public sealed record ReplayCannedFailure(string Variant, string? Detail) : BackendError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "replay canned failure: {Variant}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Variant };
}
