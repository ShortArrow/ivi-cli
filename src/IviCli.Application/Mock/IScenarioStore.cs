using System.Collections.Immutable;
using IviCli.Domain;
using IviCli.Domain.Mock;

namespace IviCli.Application.Mock;

/// <summary>
/// Port for persisting mock scenarios. Implementations live in the
/// Infrastructure layer (<c>TomlScenarioStore</c>) or as test doubles
/// (<c>FakeScenarioStore</c> in <c>IviCli.TestKit</c>).
/// </summary>
public interface IScenarioStore
{
    /// <summary>Lists every scenario name currently in the store, sorted.</summary>
    Task<Result<ImmutableArray<ScenarioName>, ScenarioStoreError>> ListAsync(CancellationToken ct);

    /// <summary>Loads a scenario by name.</summary>
    Task<Result<MockScenario, ScenarioStoreError>> LoadAsync(
        ScenarioName name,
        CancellationToken ct
    );

    /// <summary>
    /// Persists a scenario. Whether the store overwrites an existing scenario
    /// or rejects the call as <see cref="ScenarioAlreadyExists"/> is controlled
    /// by <paramref name="overwriteIfExists"/>.
    /// </summary>
    Task<Result<Unit, ScenarioStoreError>> SaveAsync(
        MockScenario scenario,
        bool overwriteIfExists,
        CancellationToken ct
    );

    /// <summary>Deletes the named scenario.</summary>
    Task<Result<Unit, ScenarioStoreError>> DeleteAsync(ScenarioName name, CancellationToken ct);

    /// <summary>Returns <see langword="true"/> when the named scenario exists.</summary>
    Task<Result<bool, ScenarioStoreError>> ExistsAsync(ScenarioName name, CancellationToken ct);

    /// <summary>
    /// Appends a single <see cref="MockScene"/> to the named scenario,
    /// creating an empty scenario when it does not yet exist (per ADR 0027
    /// §4). Implementations should perform load + add + save atomically
    /// enough for serial recording use cases; concurrent recorders against
    /// the same scenario are not supported in v1.
    /// </summary>
    Task<Result<MockScenario, ScenarioStoreError>> AppendSceneAsync(
        ScenarioName name,
        MockScene scene,
        CancellationToken ct
    );
}
