using IviCli.Domain;
using IviCli.Domain.Configuration;

namespace IviCli.Application.Configuration;

/// <summary>
/// Port for loading and persisting the project's <see cref="ConfigDocument"/>.
/// </summary>
/// <remarks>
/// The Application layer depends only on this abstraction. The actual file
/// format and on-disk location live in Infrastructure adapters
/// (e.g. <c>TomlConfigStore</c>); tests substitute the in-memory
/// <c>FakeConfigStore</c> from <c>IviCli.TestKit</c>.
/// </remarks>
public interface IConfigStore
{
    /// <summary>
    /// Loads the current configuration.
    /// </summary>
    /// <param name="ct">Cancellation token (mandatory per ADR 0023 §7).</param>
    /// <returns>
    /// <see cref="Result{T, TError}.Ok"/> with the loaded <see cref="ConfigDocument"/>;
    /// otherwise <see cref="Result{T, TError}.Error"/> wrapping a <see cref="ConfigStoreError"/>.
    /// </returns>
    Task<Result<ConfigDocument, ConfigStoreError>> LoadAsync(CancellationToken ct);

    /// <summary>
    /// Persists the supplied configuration.
    /// </summary>
    /// <param name="document">The configuration to write.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="Result{T, TError}.Ok"/> on success;
    /// otherwise <see cref="Result{T, TError}.Error"/> wrapping a <see cref="ConfigStoreError"/>.
    /// </returns>
    Task<Result<Unit, ConfigStoreError>> SaveAsync(ConfigDocument document, CancellationToken ct);
}
