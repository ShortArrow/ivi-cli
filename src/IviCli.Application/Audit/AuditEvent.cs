namespace IviCli.Application.Audit;

/// <summary>
/// An auditable security / operational event (ADR 0043). Every
/// variant carries an explicit <see cref="Timestamp"/> so consumers
/// can replay timelines without relying on log-file mtimes.
/// </summary>
public abstract record AuditEvent(DateTimeOffset Timestamp)
{
    /// <summary>The canonical event kind string emitted in the NDJSON <c>kind</c> field.</summary>
    public abstract string Kind { get; }
}

/// <summary>
/// A request authenticated successfully via the configured mechanism.
/// </summary>
/// <param name="Timestamp">UTC instant the event was observed.</param>
/// <param name="Mechanism">e.g. <c>"pat"</c>, <c>"mtls"</c>.</param>
/// <param name="Subject">Identifier visible to the operator (PAT label, mTLS cert CN, ...).</param>
/// <param name="Transport">e.g. <c>"http"</c>, <c>"websocket"</c>.</param>
public sealed record AuthSucceeded(
    DateTimeOffset Timestamp,
    string Mechanism,
    string Subject,
    string Transport
) : AuditEvent(Timestamp)
{
    /// <inheritdoc/>
    public override string Kind => "auth.succeeded";
}

/// <summary>An authentication attempt rejected by the gate.</summary>
public sealed record AuthFailed(
    DateTimeOffset Timestamp,
    string Mechanism,
    string Reason,
    string Transport
) : AuditEvent(Timestamp)
{
    /// <inheritdoc/>
    public override string Kind => "auth.failed";
}

/// <summary>A mutation against the <c>config.toml</c> document.</summary>
public sealed record ConfigMutated(DateTimeOffset Timestamp, string Operation, string Target)
    : AuditEvent(Timestamp)
{
    /// <inheritdoc/>
    public override string Kind => "config.mutated";
}

/// <summary>A Management API HTTP request observed by the request-logging middleware.</summary>
public sealed record ApiRequest(
    DateTimeOffset Timestamp,
    string Method,
    string Path,
    int Status,
    string? Subject,
    int LatencyMs
) : AuditEvent(Timestamp)
{
    /// <inheritdoc/>
    public override string Kind => "api.request";
}

/// <summary>A gateway server lifecycle transition (start / stop).</summary>
public sealed record ServerLifecycle(DateTimeOffset Timestamp, string Server, string Action)
    : AuditEvent(Timestamp)
{
    /// <inheritdoc/>
    public override string Kind => "server.lifecycle";
}
