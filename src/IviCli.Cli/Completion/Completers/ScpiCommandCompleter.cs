using System.Collections.Immutable;
using IviCli.Domain.Scpi;

namespace IviCli.Cli.Completion.Completers;

/// <summary>
/// Tab-completion source for SCPI command roots (IEEE 488.2 common
/// commands + SCPI Volume 1 standard nodes). v1 returns prefix-matched
/// long-form mnemonics from <see cref="ScpiVocabulary"/>. Currently
/// registered in DI but not bound to a verb positional — wiring this
/// up to <c>visa write</c> / <c>visa query</c> requires extending the
/// <see cref="CommandTreeWalker.ResolveSlot"/> to recognise second-
/// positional arguments and is tracked as a v2 follow-up in ADR 0032.
/// </summary>
public sealed class ScpiCommandCompleter : IDynamicCompleter
{
    /// <inheritdoc/>
    public string Name => "scpi";

    /// <inheritdoc/>
    public Task<ImmutableArray<string>> CompleteAsync(string prefix, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ScpiVocabulary.RootsStartingWith(prefix));
    }
}
