namespace IviCli.Domain.Mock;

/// <summary>
/// A single rule within a <see cref="MockScene"/>: a SCPI match string
/// and the action the FakeBackend should take when an instrument
/// operation matches it. Was called <c>MockScene</c> in v0.1.x;
/// renamed in v0.2.0 to reclaim "scene" for the state-node concept
/// (see ADR 0026 and issue #26).
/// </summary>
/// <param name="Match">
/// The exact SCPI text this rule reacts to. Matching is exact except
/// for the leading-colon prefix (IEEE 488.2 §7.5 / SCPI 1999 §6.1.1),
/// which <see cref="MockScene.FindByMatch"/> normalises.
/// </param>
/// <param name="Action">
/// What the FakeBackend should do when this rule matches — respond,
/// ack, or fail. v0.2.0 keeps the same action surface as v0.1.x; the
/// state-transition variant lands in a follow-up batch (see issue
/// #26 §"Implementation plan").
/// </param>
public sealed record MockRule(string Match, RuleAction Action);
