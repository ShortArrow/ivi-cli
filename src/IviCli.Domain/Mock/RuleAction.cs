namespace IviCli.Domain.Mock;

/// <summary>
/// What the FakeBackend should do when a <see cref="MockRule"/> matches.
/// Was called <c>SceneAction</c> in v0.1.x; renamed in v0.2.0 alongside
/// the <c>MockScene</c> → <c>MockRule</c> rename.
/// </summary>
public abstract record RuleAction
{
    private RuleAction() { }

    /// <summary>Returns a textual response (legal for QueryAsync).</summary>
    public sealed record Respond(string Text) : RuleAction;

    /// <summary>Acknowledges the operation with no response (legal for WriteAsync).</summary>
    public sealed record Ack : RuleAction;

    /// <summary>
    /// Surfaces a canned backend failure. The <paramref name="Variant"/>
    /// string is mapped to a concrete BackendError variant by the
    /// FakeBackend at playback time (e.g. <c>"transport_timeout"</c> →
    /// TransportTimeout).
    /// </summary>
    public sealed record Fail(string Variant, string? Detail) : RuleAction;
}
