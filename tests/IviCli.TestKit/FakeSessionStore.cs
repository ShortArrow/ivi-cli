using IviCli.Application.Session;
using IviCli.Domain;
using IviCli.Domain.Session;

namespace IviCli.TestKit;

/// <summary>
/// In-memory <see cref="ISessionStore"/> for tests; mirrors the surface of
/// <see cref="FakeConfigStore"/> with the same Fail-Next-* fault-injection
/// affordances (ADR 0009 §6).
/// </summary>
public sealed class FakeSessionStore : ISessionStore
{
    private SessionState _state;
    private SessionStoreError? _nextLoadFailure;
    private SessionStoreError? _nextSaveFailure;

    /// <summary>Creates a store seeded with <see cref="SessionState.Empty"/>.</summary>
    public FakeSessionStore()
        : this(SessionState.Empty) { }

    /// <summary>Creates a store seeded with the supplied initial state.</summary>
    public FakeSessionStore(SessionState initial)
    {
        _state = initial;
    }

    /// <inheritdoc/>
    public Task<Result<SessionState, SessionStoreError>> LoadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_nextLoadFailure is { } failure)
        {
            _nextLoadFailure = null;
            return Task.FromResult(Result.Failure<SessionState, SessionStoreError>(failure));
        }
        return Task.FromResult(Result.Success<SessionState, SessionStoreError>(_state));
    }

    /// <inheritdoc/>
    public Task<Result<Unit, SessionStoreError>> SaveAsync(SessionState state, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_nextSaveFailure is { } failure)
        {
            _nextSaveFailure = null;
            return Task.FromResult(Result.Failure<Unit, SessionStoreError>(failure));
        }
        _state = state;
        return Task.FromResult(Result.Success<Unit, SessionStoreError>(Unit.Value));
    }

    /// <summary>Arrange that the next <see cref="LoadAsync"/> call fails.</summary>
    public void FailNextLoadWith(string reason) =>
        _nextLoadFailure = new SessionStoreReadFailure(reason);

    /// <summary>Arrange that the next <see cref="SaveAsync"/> call fails.</summary>
    public void FailNextSaveWith(string reason) =>
        _nextSaveFailure = new SessionStoreWriteFailure(reason);
}
