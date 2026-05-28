namespace IviCli.Application.Audit;

/// <summary>
/// Append-only audit log port (ADR 0043). Implementations are
/// expected to serialise events deterministically (one NDJSON entry
/// per call in the production sink) and persist them durably enough
/// that an operator reviewing a security incident can trust the
/// resulting timeline.
/// </summary>
public interface IAuditLog
{
    /// <summary>Appends a single <see cref="AuditEvent"/> to the underlying sink.</summary>
    Task AppendAsync(AuditEvent ev, CancellationToken ct);
}

/// <summary>
/// No-op implementation used when <c>[audit] enabled = false</c> or
/// during tests that do not care about audit emissions. Singleton so
/// DI registrations can take a non-nullable port.
/// </summary>
public sealed class NullAuditLog : IAuditLog
{
    /// <summary>Shared singleton.</summary>
    public static readonly NullAuditLog Instance = new();

    private NullAuditLog() { }

    /// <inheritdoc/>
    public Task AppendAsync(AuditEvent ev, CancellationToken ct) => Task.CompletedTask;
}
