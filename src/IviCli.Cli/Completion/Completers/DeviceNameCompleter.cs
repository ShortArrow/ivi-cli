using System.Collections.Immutable;
using IviCli.Application.Configuration;
using IviCli.Domain;
using IviCli.Domain.Configuration;

namespace IviCli.Cli.Completion.Completers;

/// <summary>
/// Surfaces configured device aliases (from <c>config.toml</c>) as
/// completion candidates. Used by every command that accepts a device
/// alias as a positional argument or <c>--device</c> option.
/// </summary>
public sealed class DeviceNameCompleter : IDynamicCompleter
{
    private readonly IConfigStore _store;

    /// <summary>Creates a completer bound to the production config store.</summary>
    public DeviceNameCompleter(IConfigStore store)
    {
        _store = store;
    }

    /// <inheritdoc/>
    public string Name => "device";

    /// <inheritdoc/>
    public async Task<ImmutableArray<string>> CompleteAsync(string prefix, CancellationToken ct)
    {
        var result = await _store.LoadAsync(ct);
        if (result is not Result<ConfigDocument, ConfigStoreError>.Ok { Value: var config })
        {
            return ImmutableArray<string>.Empty;
        }
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var device in config.Devices)
        {
            if (device.Name.Value.StartsWith(prefix, StringComparison.Ordinal))
            {
                builder.Add(device.Name.Value);
            }
        }
        return builder.ToImmutable().Sort(StringComparer.Ordinal);
    }
}
