using System.Collections.Concurrent;
using System.Collections.Immutable;
using IviCli.Application.Mock;
using IviCli.Domain;
using IviCli.Domain.Mock;

namespace IviCli.TestKit;

/// <summary>
/// In-memory <see cref="IScenarioStore"/> for tests. Scenarios live in a
/// dictionary keyed by name; the surface mirrors the production store.
/// </summary>
public sealed class FakeScenarioStore : IScenarioStore
{
    private readonly ConcurrentDictionary<string, MockScenario> _scenarios = new(
        StringComparer.Ordinal
    );

    /// <summary>Creates an empty store.</summary>
    public FakeScenarioStore() { }

    /// <summary>Creates a store seeded with the supplied scenarios.</summary>
    public FakeScenarioStore(IEnumerable<MockScenario> seed)
    {
        foreach (var s in seed)
        {
            _scenarios[s.Name.Value] = s;
        }
    }

    /// <inheritdoc/>
    public Task<Result<ImmutableArray<ScenarioName>, ScenarioStoreError>> ListAsync(
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var names = _scenarios
            .Values.Select(s => s.Name)
            .OrderBy(n => n.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        return Task.FromResult(
            Result.Success<ImmutableArray<ScenarioName>, ScenarioStoreError>(names)
        );
    }

    /// <inheritdoc/>
    public Task<Result<MockScenario, ScenarioStoreError>> LoadAsync(
        ScenarioName name,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        if (!_scenarios.TryGetValue(name.Value, out var scenario))
        {
            return Task.FromResult(
                Result.Failure<MockScenario, ScenarioStoreError>(new ScenarioNotFound(name.Value))
            );
        }
        return Task.FromResult(Result.Success<MockScenario, ScenarioStoreError>(scenario));
    }

    /// <inheritdoc/>
    public Task<Result<Unit, ScenarioStoreError>> SaveAsync(
        MockScenario scenario,
        bool overwriteIfExists,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        if (!overwriteIfExists && _scenarios.ContainsKey(scenario.Name.Value))
        {
            return Task.FromResult(
                Result.Failure<Unit, ScenarioStoreError>(
                    new ScenarioAlreadyExists(scenario.Name.Value)
                )
            );
        }
        _scenarios[scenario.Name.Value] = scenario;
        return Task.FromResult(Result.Success<Unit, ScenarioStoreError>(Unit.Value));
    }

    /// <inheritdoc/>
    public Task<Result<Unit, ScenarioStoreError>> DeleteAsync(
        ScenarioName name,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        if (!_scenarios.TryRemove(name.Value, out _))
        {
            return Task.FromResult(
                Result.Failure<Unit, ScenarioStoreError>(new ScenarioNotFound(name.Value))
            );
        }
        return Task.FromResult(Result.Success<Unit, ScenarioStoreError>(Unit.Value));
    }

    /// <inheritdoc/>
    public Task<Result<bool, ScenarioStoreError>> ExistsAsync(
        ScenarioName name,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(
            Result.Success<bool, ScenarioStoreError>(_scenarios.ContainsKey(name.Value))
        );
    }

    /// <inheritdoc/>
    public Task<Result<MockScenario, ScenarioStoreError>> AppendSceneAsync(
        ScenarioName name,
        MockScene scene,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var scenario = _scenarios.TryGetValue(name.Value, out var existing)
            ? existing
            : MockScenario.Empty(name);
        var updated = scenario.AddScene(scene);
        _scenarios[name.Value] = updated;
        return Task.FromResult(Result.Success<MockScenario, ScenarioStoreError>(updated));
    }
}
