using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.CommandLine;

namespace IviCli.Cli.Completion;

/// <summary>
/// A runtime source of completion candidates that the <c>__complete</c>
/// verb consults when the cursor sits on a positional argument or an
/// option value that the static command tree alone cannot resolve.
/// Each implementation answers for one logical kind of identifier
/// (device alias, server alias, scenario name, …).
/// </summary>
/// <remarks>
/// Adding a new dynamic-completion source must require **only one new
/// file** that implements this interface plus a DI registration in
/// <c>Program.cs</c>. Pinning the source to a specific
/// <see cref="Command"/> + argument name lives in
/// <see cref="CompletionRegistry"/>, not in the implementation itself.
/// </remarks>
public interface IDynamicCompleter
{
    /// <summary>Stable identifier — used by the registry to wire bindings.</summary>
    string Name { get; }

    /// <summary>Returns candidates that start with <paramref name="prefix"/>.</summary>
    Task<ImmutableArray<string>> CompleteAsync(string prefix, CancellationToken ct);
}

/// <summary>
/// Maps a (command, argument-or-option name) pair to the dynamic
/// completer that should answer for it. The registry is populated by
/// each CLI verb's Build method when the root command is constructed,
/// so the binding lives next to the verb definition rather than in a
/// central switch.
/// </summary>
public sealed class CompletionRegistry
{
    private readonly ConcurrentDictionary<
        (Command Command, string Slot),
        IDynamicCompleter
    > _bindings = new();

    /// <summary>
    /// Registers <paramref name="completer"/> as the source for the
    /// supplied <paramref name="slot"/> on <paramref name="command"/>.
    /// The slot name is the argument or option name (without leading
    /// dashes for options).
    /// </summary>
    public void Bind(Command command, string slot, IDynamicCompleter completer)
    {
        _bindings[(command, slot)] = completer;
    }

    /// <summary>
    /// Looks up the dynamic completer for the supplied
    /// <paramref name="command"/> / <paramref name="slot"/> pair.
    /// Returns <see langword="null"/> when no binding exists.
    /// </summary>
    public IDynamicCompleter? Resolve(Command command, string slot)
    {
        return _bindings.TryGetValue((command, slot), out var c) ? c : null;
    }
}
