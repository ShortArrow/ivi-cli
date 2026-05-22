using IviCli.Domain;
using IviCli.Domain.Session;

namespace IviCli.Application.Session;

/// <summary>
/// Port for loading and persisting the project's <see cref="SessionState"/>.
/// </summary>
public interface ISessionStore
{
    /// <summary>Loads the current session state.</summary>
    Task<Result<SessionState, SessionStoreError>> LoadAsync(CancellationToken ct);

    /// <summary>Persists the supplied session state.</summary>
    Task<Result<Unit, SessionStoreError>> SaveAsync(SessionState state, CancellationToken ct);
}
