using System.Collections.Immutable;
using IviCli.Application.Mock;
using IviCli.Domain;
using IviCli.Domain.Mock;

namespace IviCli.Cli.Completion.Completers;

/// <summary>
/// Surfaces stored mock-scenario names as completion candidates. Used
/// by every <c>mock scenario *</c> verb that takes a scenario name
/// positional argument.
/// </summary>
public sealed class ScenarioNameCompleter : IDynamicCompleter
{
    private readonly IScenarioStore _store;

    /// <summary>Creates a completer bound to the production scenario store.</summary>
    public ScenarioNameCompleter(IScenarioStore store)
    {
        _store = store;
    }

    /// <inheritdoc/>
    public string Name => "scenario";

    /// <inheritdoc/>
    public async Task<ImmutableArray<string>> CompleteAsync(string prefix, CancellationToken ct)
    {
        var result = await _store.ListAsync(ct);
        if (
            result
            is not Result<ImmutableArray<ScenarioName>, ScenarioStoreError>.Ok { Value: var names }
        )
        {
            return ImmutableArray<string>.Empty;
        }
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var name in names)
        {
            if (name.Value.StartsWith(prefix, StringComparison.Ordinal))
            {
                builder.Add(name.Value);
            }
        }
        return builder.ToImmutable().Sort(StringComparer.Ordinal);
    }
}
