using System.Collections.Immutable;

namespace IviCli.Domain.Mock;

/// <summary>
/// A named collection of <see cref="MockScene"/>s that FakeBackend consults
/// when it is the active scenario. The shape is intentionally flat (no
/// ordering, no state machine) per ADR 0026 §3 v1.
/// </summary>
/// <param name="Name">The scenario's unique alias.</param>
/// <param name="IdnDefault">
/// Optional default <c>*IDN?</c> response. Used when no scene explicitly
/// matches <c>*IDN?</c>.
/// </param>
/// <param name="Scenes">The set of scenes, in insertion order.</param>
public sealed record MockScenario(
    ScenarioName Name,
    string? IdnDefault,
    ImmutableArray<MockScene> Scenes
)
{
    /// <summary>Creates an empty scenario with the supplied name.</summary>
    public static MockScenario Empty(ScenarioName name) =>
        new(name, IdnDefault: null, Scenes: ImmutableArray<MockScene>.Empty);

    /// <summary>
    /// Returns a new <see cref="MockScenario"/> with <paramref name="scene"/> appended.
    /// </summary>
    public MockScenario AddScene(MockScene scene) => this with { Scenes = Scenes.Add(scene) };

    /// <summary>
    /// Returns a new <see cref="MockScenario"/> with the 1-based scene removed,
    /// or <see langword="null"/> when the index is out of range.
    /// </summary>
    public MockScenario? RemoveSceneAt(int oneBasedIndex)
    {
        if (oneBasedIndex < 1 || oneBasedIndex > Scenes.Length)
        {
            return null;
        }
        return this with { Scenes = Scenes.RemoveAt(oneBasedIndex - 1) };
    }

    /// <summary>Looks up a scene by its exact SCPI text.</summary>
    public MockScene? FindByMatch(string scpi)
    {
        foreach (var s in Scenes)
        {
            if (s.Match == scpi)
            {
                return s;
            }
        }
        return null;
    }

    /// <summary>Custom structural equality including the array contents.</summary>
    public bool Equals(MockScenario? other) =>
        other is not null
        && Name == other.Name
        && IdnDefault == other.IdnDefault
        && Scenes.SequenceEqual(other.Scenes);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(IdnDefault);
        foreach (var s in Scenes)
        {
            hash.Add(s);
        }
        return hash.ToHashCode();
    }
}
