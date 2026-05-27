using System.Buffers;
using System.Collections.Immutable;

namespace IviCli.Domain.Scpi;

/// <summary>
/// Canonical SCPI mnemonic vocabulary used by the script linter and the
/// <c>visa write</c> / <c>visa query</c> Tab-completion plumbing. Holds
/// IEEE 488.2 §10 mandatory common commands and the SCPI Volume 1 §15
/// standard root nodes. Vendor-specific extensions are <b>not</b>
/// included — those require per-vendor dictionaries this project does
/// not own (ADR 0032).
/// </summary>
public static class ScpiVocabulary
{
    /// <summary>
    /// IEEE 488.2 §10 mandatory common commands. Stored as written
    /// (case-sensitive lookups go through <see cref="IsKnownRoot"/>
    /// which normalises to upper-case first).
    /// </summary>
    public static readonly ImmutableArray<string> CommonCommands = ImmutableArray.Create(
        "*CLS",
        "*ESE",
        "*ESE?",
        "*ESR?",
        "*IDN?",
        "*OPC",
        "*OPC?",
        "*PSC",
        "*PSC?",
        "*RST",
        "*SRE",
        "*SRE?",
        "*STB?",
        "*TST?",
        "*WAI"
    );

    /// <summary>
    /// SCPI Volume 1 §15 standard root nodes, each as a (long, short)
    /// pair. The short form is the upper-case prefix permitted by the
    /// SCPI mnemonic convention.
    /// </summary>
    public static readonly ImmutableArray<(string Long, string Short)> CoreRoots =
        ImmutableArray.Create<(string, string)>(
            ("SYSTem", "SYST"),
            ("STATus", "STAT"),
            ("MEASure", "MEAS"),
            ("SENSe", "SENS"),
            ("SOURce", "SOUR"),
            ("OUTPut", "OUTP"),
            ("INPut", "INP"),
            ("CONFigure", "CONF"),
            ("READ", "READ"),
            ("FETCh", "FETC"),
            ("INITiate", "INIT"),
            ("TRIGger", "TRIG"),
            ("CALCulate", "CALC"),
            ("DISPlay", "DISP"),
            ("FORMat", "FORM"),
            ("MEMory", "MEM"),
            ("ROUTe", "ROUT"),
            ("UNIT", "UNIT"),
            ("HCOPy", "HCOP"),
            ("CALibrate", "CAL"),
            ("PROGram", "PROG"),
            ("INSTrument", "INST"),
            ("ABORt", "ABOR")
        );

    /// <summary>
    /// All long-form mnemonics in alphabetical order. Use for diagnostics
    /// or to render the full vocabulary.
    /// </summary>
    public static readonly ImmutableArray<string> AllRoots = BuildAllRoots();

    private static readonly HashSet<string> KnownUpper = BuildKnownUpper();
    private static readonly SearchValues<char> Whitespace = SearchValues.Create(" \t");

    /// <summary>
    /// Returns true when <paramref name="mnemonic"/>'s first colon-segment
    /// matches a known common command or core root (case-insensitive).
    /// Leading <c>:</c>, trailing <c>?</c>, and parameters after a space
    /// are tolerated. Returns false on null / whitespace.
    /// </summary>
    public static bool IsKnownRoot(string? mnemonic)
    {
        var root = ExtractRoot(mnemonic);
        if (root is null)
        {
            return false;
        }
        return KnownUpper.Contains(root);
    }

    /// <summary>
    /// Returns long-form mnemonics that begin with <paramref name="prefix"/>
    /// (case-insensitive), sorted ordinally. Empty prefix returns
    /// <see cref="AllRoots"/>.
    /// </summary>
    public static ImmutableArray<string> RootsStartingWith(string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return AllRoots;
        }
        var upper = prefix.ToUpperInvariant();
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var root in AllRoots)
        {
            if (root.ToUpperInvariant().StartsWith(upper, StringComparison.Ordinal))
            {
                builder.Add(root);
            }
        }
        return builder.ToImmutable();
    }

    /// <summary>
    /// Extracts the leading colon-segment of <paramref name="mnemonic"/>
    /// in upper case, stripped of a trailing <c>?</c> and any leading
    /// <c>:</c>. Returns <see langword="null"/> on null / whitespace.
    /// </summary>
    public static string? ExtractRoot(string? mnemonic)
    {
        if (string.IsNullOrWhiteSpace(mnemonic))
        {
            return null;
        }
        var trimmed = mnemonic.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }
        // Strip parameters (anything past the first whitespace).
        var firstSpace = trimmed.AsSpan().IndexOfAny(Whitespace);
        if (firstSpace > 0)
        {
            trimmed = trimmed[..firstSpace];
        }
        // For common commands like *IDN? we keep the whole token; for
        // hierarchical mnemonics we split on the first ':' after stripping
        // an optional leading ':'.
        if (trimmed.StartsWith('*'))
        {
            return trimmed.ToUpperInvariant();
        }
        if (trimmed.StartsWith(':'))
        {
            trimmed = trimmed[1..];
        }
        var colon = trimmed.IndexOf(':');
        var root = colon >= 0 ? trimmed[..colon] : trimmed;
        // Drop trailing '?' so SYST:ERR? and SYST resolve identically.
        if (root.EndsWith('?'))
        {
            root = root[..^1];
        }
        return root.ToUpperInvariant();
    }

    private static ImmutableArray<string> BuildAllRoots()
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        builder.AddRange(CommonCommands);
        foreach (var (longForm, _) in CoreRoots)
        {
            builder.Add(longForm);
        }
        builder.Sort(StringComparer.Ordinal);
        return builder.ToImmutable();
    }

    private static HashSet<string> BuildKnownUpper()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var common in CommonCommands)
        {
            // Common commands keep the '*' prefix; lookups also strip trailing '?'.
            set.Add(common.ToUpperInvariant());
            if (common.EndsWith('?'))
            {
                set.Add(common[..^1].ToUpperInvariant());
            }
        }
        foreach (var (longForm, shortForm) in CoreRoots)
        {
            set.Add(longForm.ToUpperInvariant());
            set.Add(shortForm.ToUpperInvariant());
        }
        return set;
    }
}
