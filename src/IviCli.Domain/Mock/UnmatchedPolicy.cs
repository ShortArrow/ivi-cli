namespace IviCli.Domain.Mock;

/// <summary>
/// What the mock does with a request that no rule in the current scene
/// matches (ADR 0026 §4).
/// </summary>
public enum UnmatchedPolicy
{
    /// <summary>
    /// An unmatched query is answered with its own text, and an unmatched
    /// write succeeds.
    /// </summary>
    Echo,

    /// <summary>
    /// An unmatched query or write fails, so a test of instrument-control
    /// code cannot pass on a request the scenario never declared. Only the
    /// scenario's <c>idn</c> answers <c>*IDN?</c> without a rule.
    /// </summary>
    Fail,
}
