using System.Collections.Immutable;
using IviCli.Application.Configuration;
using IviCli.Domain;
using IviCli.Domain.Configuration;

namespace IviCli.Cli.Completion.Completers;

/// <summary>
/// Surfaces configured gateway-server aliases (from <c>config.toml</c>)
/// as completion candidates. Used by <c>server start / stop / log</c>.
/// </summary>
public sealed class ServerNameCompleter : IDynamicCompleter
{
    private readonly IConfigStore _store;

    /// <summary>Creates a completer bound to the production config store.</summary>
    public ServerNameCompleter(IConfigStore store)
    {
        _store = store;
    }

    /// <inheritdoc/>
    public string Name => "server";

    /// <inheritdoc/>
    public async Task<ImmutableArray<string>> CompleteAsync(string prefix, CancellationToken ct)
    {
        var result = await _store.LoadAsync(ct);
        if (result is not Result<ConfigDocument, ConfigStoreError>.Ok { Value: var config })
        {
            return ImmutableArray<string>.Empty;
        }
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var server in config.Servers)
        {
            if (server.Name.Value.StartsWith(prefix, StringComparison.Ordinal))
            {
                builder.Add(server.Name.Value);
            }
        }
        return builder.ToImmutable().Sort(StringComparer.Ordinal);
    }
}
