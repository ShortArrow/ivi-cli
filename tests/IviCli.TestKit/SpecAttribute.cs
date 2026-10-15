using Xunit.v3;

namespace IviCli.TestKit;

/// <summary>A source a conformance entry cites (ADR 0054).</summary>
public enum Standard
{
    /// <summary>IVI Foundation VPP-4.3, <i>The VISA Library</i>.</summary>
    Visa,

    /// <summary>IVI Foundation IVI-6.1, <i>High-Speed LAN Instrument Protocol</i>.</summary>
    HiSlip,

    /// <summary>VXIbus Consortium, <i>TCP/IP Instrument Protocol Specification</i>.</summary>
    Vxi11,

    /// <summary>An ivi-cli ADR, for a rule ivi-cli defines where the standards are silent.</summary>
    Adr,
}

/// <summary>
/// Names a clause the test pins, as the trait <c>Spec</c> with the value
/// <c>&lt;label&gt; &lt;clause&gt;</c>, for example <c>VXI-11 RULE B.6.19</c>.
/// CI checks every such value against the <c>Spec</c> column of
/// <c>docs/conformance.md</c> (ADR 0054).
/// </summary>
/// <param name="standard">The source the clause belongs to.</param>
/// <param name="clause">
/// The clause written in full as the source prints it, such as
/// <c>RULE B.6.27</c> or <c>§5.1.2</c>; for <see cref="Standard.Adr"/>,
/// the ADR number and section, such as <c>0053 §3</c>.
/// </param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class SpecAttribute(Standard standard, string clause) : Attribute, ITraitAttribute
{
    /// <summary>The trait key every <see cref="SpecAttribute"/> uses.</summary>
    public const string TraitName = "Spec";

    /// <summary>The trait value, <c>&lt;label&gt; &lt;clause&gt;</c>.</summary>
    public string Value { get; } = $"{Label(standard)} {clause}";

    /// <inheritdoc/>
    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() => [new(TraitName, Value)];

    private static string Label(Standard standard) =>
        standard switch
        {
            Standard.Visa => "VPP-4.3",
            Standard.HiSlip => "IVI-6.1",
            Standard.Vxi11 => "VXI-11",
            Standard.Adr => "ADR",
            _ => throw new ArgumentOutOfRangeException(nameof(standard), standard, null),
        };
}
