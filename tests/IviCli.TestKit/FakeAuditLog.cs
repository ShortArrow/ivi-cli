using IviCli.Application.Audit;

namespace IviCli.TestKit;

/// <summary>
/// In-memory <see cref="IAuditLog"/> for tests. Captures every emitted
/// <see cref="AuditEvent"/> in insertion order; thread-safe so middleware
/// pipelines and concurrent handler tests can share one instance.
/// </summary>
public sealed class FakeAuditLog : IAuditLog
{
    private readonly List<AuditEvent> _events = new();
    private readonly object _gate = new();

    /// <summary>Snapshot of every event appended so far, in insertion order.</summary>
    public IReadOnlyList<AuditEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return _events.ToArray();
            }
        }
    }

    /// <inheritdoc/>
    public Task AppendAsync(AuditEvent ev, CancellationToken ct)
    {
        lock (_gate)
        {
            _events.Add(ev);
        }
        return Task.CompletedTask;
    }
}
