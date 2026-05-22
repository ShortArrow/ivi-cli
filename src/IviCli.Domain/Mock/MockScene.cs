namespace IviCli.Domain.Mock;

/// <summary>
/// A single scene in a mock scenario: a SCPI match string and the action the
/// FakeBackend should take when an instrument operation matches it.
/// </summary>
/// <param name="Match">The exact SCPI text this scene reacts to (v1 is exact-match only).</param>
/// <param name="Action">What the FakeBackend should do — respond, ack, or fail.</param>
public sealed record MockScene(string Match, SceneAction Action);

/// <summary>What the FakeBackend should do when a scene matches.</summary>
public abstract record SceneAction
{
    private SceneAction() { }

    /// <summary>Returns a textual response (legal for QueryAsync).</summary>
    public sealed record Respond(string Text) : SceneAction;

    /// <summary>Acknowledges the operation with no response (legal for WriteAsync).</summary>
    public sealed record Ack : SceneAction;

    /// <summary>
    /// Surfaces a canned backend failure. The <paramref name="Variant"/>
    /// string is mapped to a concrete BackendError variant by the FakeBackend
    /// at playback time (e.g. <c>"transport_timeout"</c> → TransportTimeout).
    /// </summary>
    public sealed record Fail(string Variant, string? Detail) : SceneAction;
}
