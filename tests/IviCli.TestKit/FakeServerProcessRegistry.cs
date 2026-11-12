using System.Collections.Immutable;
using IviCli.Application.Servers;
using IviCli.Domain;
using IviCli.Domain.Servers;

namespace IviCli.TestKit;

/// <summary>In-memory <see cref="IServerProcessRegistry"/> for handler tests.</summary>
public sealed class FakeServerProcessRegistry : IServerProcessRegistry
{
    private readonly Dictionary<string, ServerProcessEntry> _entries = new();
    private ServerProcessRegistryError? _nextReadFailure;

    /// <summary>Makes the next <see cref="ReadAsync"/> fail with an IO error.</summary>
    public void FailNextReadWith(string detail) =>
        _nextReadFailure = new ServerProcessRegistryIoFailure(detail, null);

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public Task<Result<ServerProcessEntry?, ServerProcessRegistryError>> ReadAsync(
        ServerName name,
        CancellationToken ct
    )
    {
        if (_nextReadFailure is { } failure)
        {
            _nextReadFailure = null;
            return Task.FromResult(
                Result.Failure<ServerProcessEntry?, ServerProcessRegistryError>(failure)
            );
        }
        return Task.FromResult(
            Result.Success<ServerProcessEntry?, ServerProcessRegistryError>(
                _entries.TryGetValue(name.Value, out var e) ? e : null
            )
        );
    }

    /// <inheritdoc/>
    public Task<Result<Unit, ServerProcessRegistryError>> DeleteAsync(
        ServerName name,
        CancellationToken ct
    )
    {
        _entries.Remove(name.Value);
        return Task.FromResult(Result.Success<Unit, ServerProcessRegistryError>(Unit.Value));
    }

    /// <inheritdoc/>
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
