using System.Collections.Immutable;

namespace IviCli.Domain.Mock;

/// <summary>
/// A named behaviour package that the FakeBackend consults when it is
/// the active scenario. In v0.2.0 a scenario is a collection of
/// <see cref="MockScene"/>s (state nodes) plus a designated starting
/// scene; in v0.1.x it was a flat list of (match → action) rules. The
/// v0.1.x model collapses into a single synthetic <c>default</c>
/// scene at parse time so existing TOML files keep loading (see
/// ADR 0026 and issue #26).
/// </summary>
/// <param name="Name">The scenario's unique alias.</param>
/// <param name="InitialScene">
/// The scene that is active when <c>ivicli mock scenario activate</c>
/// runs. Once state-transition rule actions land
/// (issue #26 §"Implementation plan" — B0.2-3), rules can move the
/// FakeBackend to a different scene at runtime.
/// </param>
/// <param name="IdnDefault">
/// Optional default <c>*IDN?</c> response. Used when no rule explicitly
/// matches <c>*IDN?</c>.
/// </param>
/// <param name="Scenes">The set of scenes that make up this scenario.</param>
/// <param name="Quirks">
/// Optional firmware misbehaviour the mock reproduces while this
/// scenario is bound (issue #115). <see langword="null"/> — the default
/// — means an ideally behaved mock.
/// </param>
public sealed record MockScenario(
    ScenarioName Name,
    SceneName InitialScene,
    string? IdnDefault,
    ImmutableArray<MockScene> Scenes,
    MockQuirks? Quirks = null
)
{
    /// <summary>Creates an empty scenario with the supplied name and a
    /// single empty <c>default</c> scene.</summary>
    public static MockScenario Empty(ScenarioName name)
    {
        var defaultScene = SceneName.DefaultScene();
        return new(
            name,
            InitialScene: defaultScene,
            IdnDefault: null,
            Scenes: ImmutableArray.Create(MockScene.Empty(defaultScene))
        );
    }

    /// <summary>
    /// Convenience factory for the v0.1.x "flat rules" shape — wraps
    /// the supplied <paramref name="rules"/> in a single
    /// <c>default</c> scene and designates it as the initial scene.
    /// </summary>
    public static MockScenario SingleScene(
        ScenarioName name,
        string? idnDefault,
        ImmutableArray<MockRule> rules
    )
    {
        var defaultScene = SceneName.DefaultScene();
        return new(
            name,
            InitialScene: defaultScene,
            IdnDefault: idnDefault,
            Scenes: ImmutableArray.Create(new MockScene(defaultScene, rules))
        );
    }

    /// <summary>Returns the scene with the supplied name, or
    /// <see langword="null"/> when no such scene exists.</summary>
    public MockScene? FindScene(SceneName name)
    {
        foreach (var s in Scenes)
        {
            if (s.Name == name)
            {
                return s;
            }
        }
        return null;
    }

    /// <summary>
    /// Looks up a rule in the initial scene by SCPI text. Provided as
    /// a convenience for callers that have not yet adopted the
    /// state-machine semantics (B0.2-3) and treat the scenario as a
    /// flat rule list. Equivalent to
    /// <c>FindScene(InitialScene).FindByMatch(scpi)</c>.
    /// </summary>
    public MockRule? FindByMatch(string scpi) => FindScene(InitialScene)?.FindByMatch(scpi);

    /// <summary>Returns a new scenario with the supplied scene appended.</summary>
    public MockScenario AddScene(MockScene scene) => this with { Scenes = Scenes.Add(scene) };

    /// <summary>
    /// Returns a new scenario with the named scene replaced by
    /// <paramref name="replacement"/>, or <see langword="null"/> when
    /// no scene of that name exists. Useful when appending a rule via
    /// <see cref="MockScene.AddRule"/> on an existing scene.
    /// </summary>
    public MockScenario? ReplaceScene(MockScene replacement)
    {
        for (var i = 0; i < Scenes.Length; i++)
        {
            if (Scenes[i].Name == replacement.Name)
            {
                return this with { Scenes = Scenes.SetItem(i, replacement) };
            }
        }
        return null;
    }

    /// <summary>Custom structural equality including the array contents.</summary>
    public bool Equals(MockScenario? other) =>
        other is not null
        && Name == other.Name
        && InitialScene == other.InitialScene
        && IdnDefault == other.IdnDefault
        && Quirks == other.Quirks
        && Scenes.SequenceEqual(other.Scenes);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(InitialScene);
        hash.Add(IdnDefault);
        hash.Add(Quirks);
        foreach (var s in Scenes)
        {
            hash.Add(s);
        }
        return hash.ToHashCode();
    }
}
