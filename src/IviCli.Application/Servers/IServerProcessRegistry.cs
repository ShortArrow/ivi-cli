using System.Collections.Immutable;
using IviCli.Domain;
using IviCli.Domain.Servers;

namespace IviCli.Application.Servers;

/// <summary>
/// Port for tracking the OS process that owns a running gateway server
/// (PRD §7.3 / ADR 0027 §1 — server stop closure). Implementations live
/// in Infrastructure; the file-backed default writes
/// <c>&lt;state-dir&gt;/&lt;server-name&gt;.pid</c>.
/// </summary>
public interface IServerProcessRegistry
{
    /// <summary>
    /// Records that the supplied <paramref name="name"/> is owned by
    /// <paramref name="processId"/> as of <paramref name="startedAt"/>.
    /// Overwrites any existing entry with the same name.
    /// </summary>
    Task<Result<Unit, ServerProcessRegistryError>> WriteAsync(
        ServerName name,
        int processId,
        DateTimeOffset startedAt,
        CancellationToken ct
    );

    /// <summary>Reads the entry for <paramref name="name"/>, if any.</summary>
    Task<Result<ServerProcessEntry?, ServerProcessRegistryError>> ReadAsync(
        ServerName name,
        CancellationToken ct
    );

    /// <summary>Removes the entry for <paramref name="name"/>; idempotent.</summary>
    Task<Result<Unit, ServerProcessRegistryError>> DeleteAsync(
        ServerName name,
        CancellationToken ct
    );

    /// <summary>Lists every recorded entry, sorted by server name.</summary>
    Task<Result<ImmutableArray<ServerProcessEntry>, ServerProcessRegistryError>> ListAsync(
        CancellationToken ct
    );
}

/// <summary>A single registry entry — the PID file's payload.</summary>
public sealed record ServerProcessEntry(ServerName Name, int ProcessId, DateTimeOffset StartedAt);

/// <summary>Error variants for <see cref="IServerProcessRegistry"/>.</summary>
public abstract record ServerProcessRegistryError : IviError
{
    /// <inheritdoc/>
    public abstract LogSeverity Severity { get; }

    /// <inheritdoc/>
    public abstract string Message { get; }

    /// <inheritdoc/>
    public virtual IReadOnlyList<object?> LogArgs => Array.Empty<object?>();

    /// <inheritdoc/>
    public virtual Exception? Cause => null;
}

/// <summary>An IO failure was encountered while reading or writing.</summary>
public sealed record ServerProcessRegistryIoFailure(string Detail, Exception? Inner)
    : ServerProcessRegistryError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Error;

    /// <inheritdoc/>
    public override string Message => "server process registry IO failure: {Detail}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Detail };

    /// <inheritdoc/>
    public override Exception? Cause => Inner;
}

/// <summary>The PID file contents were unparseable.</summary>
public sealed record ServerProcessRegistryCorrupt(string Path, string Raw)
    : ServerProcessRegistryError
{
    /// <inheritdoc/>
    public override LogSeverity Severity => LogSeverity.Warning;

    /// <inheritdoc/>
    public override string Message => "server process registry corrupt at {Path}";

    /// <inheritdoc/>
    public override IReadOnlyList<object?> LogArgs => new object?[] { Path };
}
