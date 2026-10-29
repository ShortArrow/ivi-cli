using System.Collections.Immutable;
using IviCli.Domain.Scpi;

namespace IviCli.Domain.Mock;

/// <summary>
/// A scene inside a <see cref="MockScenario"/>: a named state node
/// holding the <see cref="MockRule"/>s that apply while the scene is
/// the scenario's current scene. v0.2.0 introduced this shape to
/// reclaim "scene" for the state-node concept; the v0.1.x
/// <c>MockScene</c> (a single match→action pair) was renamed to
/// <see cref="MockRule"/> (see ADR 0026 and issue #26).
/// </summary>
/// <param name="Name">The scene's alias inside the scenario.</param>
/// <param name="Rules">
/// The set of rules that fire while the scene is active, in insertion
/// order. Matching is per-rule and stops at the first hit
/// (see <see cref="FindByMatch(string)"/>).
/// </param>
public sealed record MockScene(SceneName Name, ImmutableArray<MockRule> Rules)
{
    /// <summary>Returns an empty scene with the supplied name.</summary>
    public static MockScene Empty(SceneName name) => new(name, ImmutableArray<MockRule>.Empty);

    /// <summary>Returns a new scene with <paramref name="rule"/> appended.</summary>
    public MockScene AddRule(MockRule rule) => this with { Rules = Rules.Add(rule) };

    /// <summary>
    /// Returns a new scene with the 1-based rule removed, or
    /// <see langword="null"/> when the index is out of range.
    /// </summary>
    public MockScene? RemoveRuleAt(int oneBasedIndex)
    {
        if (oneBasedIndex < 1 || oneBasedIndex > Rules.Length)
        {
            return null;
        }
        return this with { Rules = Rules.RemoveAt(oneBasedIndex - 1) };
    }

    /// <summary>
    /// Looks up a rule by SCPI text (ADR 0051 §1). Matching is exact
    /// except for two things an instrument ignores as well: the
    /// optional root-relative colon prefix (IEEE 488.2 §7.5 / SCPI 1999
    /// §6.1.1), and case outside quoted strings and block data
    /// (<see cref="ScpiMessage.FoldCase"/>). A rule registered as
    /// <c>OUTP ON</c> matches <c>OUTP ON</c>, <c>:OUTP ON</c> and
    /// <c>outp on</c>. Short and long forms are not equivalent. The first
    /// matching rule in <see cref="Rules"/> wins.
    /// </summary>
    public MockRule? FindByMatch(string scpi)
    {
        var normalized = NormalizeForMatch(scpi);
        foreach (var r in Rules)
        {
            if (NormalizeForMatch(r.Match) == normalized)
            {
                return r;
            }
        }
        return null;
    }

    /// <summary>Custom structural equality including the array contents.</summary>
    public bool Equals(MockScene? other) =>
        other is not null && Name == other.Name && Rules.SequenceEqual(other.Rules);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        foreach (var r in Rules)
        {
            hash.Add(r);
        }
        return hash.ToHashCode();
    }

    private static string NormalizeForMatch(string scpi) =>
        ScpiMessage.FoldCase(scpi.Length > 0 && scpi[0] == ':' ? scpi[1..] : scpi);
}
