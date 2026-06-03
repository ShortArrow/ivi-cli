namespace IviCli.Domain.Mock;

/// <summary>
/// What the FakeBackend should do when a <see cref="MockRule"/> matches.
/// Was called <c>SceneAction</c> in v0.1.x; renamed in v0.2.0 alongside
/// the <c>MockScene</c> → <c>MockRule</c> rename. Every variant carries
/// an optional <c>Transition</c> field: when set, the FakeBackend swaps
/// the active scenario's current scene to the named scene immediately
/// after the rule's effect is applied (issue #26 §"Implementation plan"
/// — B0.2-3). A rule that "answers and moves on" is therefore a single
/// rule with both the answer and the transition set; a rule that only
/// transitions can use <see cref="Ack"/> with <c>Transition</c> set.
/// </summary>
public abstract record RuleAction
{
    private RuleAction() { }

    /// <summary>
    /// The scene the FakeBackend should make current after this rule
    /// fires. <see langword="null"/> means "stay in the current scene"
    /// (the v0.1.x behaviour). Set per-variant via the record's
    /// optional constructor parameter.
    /// </summary>
    public abstract SceneName? Transition { get; }

    /// <summary>Returns a textual response (legal for QueryAsync).</summary>
    public sealed record Respond(string Text, SceneName? Transition = null) : RuleAction
    {
        /// <inheritdoc/>
        public override SceneName? Transition { get; } = Transition;
    }

    /// <summary>Acknowledges the operation with no response (legal for WriteAsync).</summary>
    public sealed record Ack(SceneName? Transition = null) : RuleAction
    {
        /// <inheritdoc/>
        public override SceneName? Transition { get; } = Transition;
    }

    /// <summary>
    /// Surfaces a canned backend failure. The <paramref name="Variant"/>
    /// string is mapped to a concrete BackendError variant by the
    /// FakeBackend at playback time (e.g. <c>"transport_timeout"</c> →
    /// TransportTimeout).
    /// </summary>
    public sealed record Fail(string Variant, string? Detail, SceneName? Transition = null)
        : RuleAction
    {
        /// <inheritdoc/>
        public override SceneName? Transition { get; } = Transition;
    }
}
