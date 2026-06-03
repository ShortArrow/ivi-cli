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

    /// <summary>
    /// Looks up a scene by SCPI text. Matching is exact except for the
    /// SCPI grammar's optional root-relative colon prefix
    /// (IEEE 488.2 §7.5 / SCPI 1999 §6.1.1): a scene registered as
    /// <c>OUTP ON</c> matches both <c>OUTP ON</c> and <c>:OUTP ON</c>
    /// at lookup time, because — at the start of a message — both
    /// forms address the same root command. Real VISA clients
    /// (NI-VISA, Keysight, PyVISA) and apps like ImageDataGetter
    /// freely emit the colon-prefixed form; honouring it here means
    /// scenarios do not need redundant <c>:OUTP ON</c> / <c>OUTP ON</c>
    /// duplicates.
    /// </summary>
    public MockScene? FindByMatch(string scpi)
    {
        var normalized = NormalizeForMatch(scpi);
        foreach (var s in Scenes)
        {
            if (NormalizeForMatch(s.Match) == normalized)
            {
                return s;
            }
        }
        return null;
    }

    private static string NormalizeForMatch(string scpi) =>
        scpi.Length > 0 && scpi[0] == ':' ? scpi[1..] : scpi;

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
