using System.Collections.Immutable;
using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using IviCli.Application.Auth;
using IviCli.Domain;
using IviCli.Domain.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace IviCli.Cli.Commands;

/// <summary>
/// Builds the <c>ivicli api token ...</c> subcommand tree (ADR 0036).
/// Three verbs:
/// <list type="bullet">
/// <item><c>create [--label "..."]</c> — mint a new token, print the raw value once.</item>
/// <item><c>list [--json]</c> — show stored tokens (hash-only, safe to render).</item>
/// <item><c>revoke &lt;id&gt;</c> — remove a token by id.</item>
/// </list>
/// </summary>
public static class ApiTokenCommand
{
    /// <summary>Builds the configured <see cref="Command"/>.</summary>
    public static Command Build(IServiceProvider services)
    {
        var command = new Command("token", "Manage Management API access tokens (ADR 0036).");
        command.Subcommands.Add(BuildCreate(services));
        command.Subcommands.Add(BuildList(services));
        command.Subcommands.Add(BuildRevoke(services));
        return command;
    }

    private static Command BuildCreate(IServiceProvider services)
    {
        var labelOpt = new Option<string?>("--label")
        {
            Description = "Human-readable label for the token (max 64 chars).",
        };
        var scopeOpt = new Option<string[]>("--scope")
        {
            Description =
                "Capability scope (repeatable). Allowed: read:devices / read:servers / "
                + "read:scenarios / write:scpi. Omit for a legacy unrestricted token (ADR 0044).",
            AllowMultipleArgumentsPerToken = true,
            Arity = ArgumentArity.ZeroOrMore,
        };
        var expiresOpt = new Option<string?>("--expires")
        {
            Description =
                "Token expiry: a duration suffix (30d / 12h / 5m), or an ISO-8601 absolute "
                + "instant. Omit for a token that never expires (ADR 0044).",
        };
        var cmd = new Command(
            "create",
            "Mint a new API token. The raw token is printed once and never recoverable."
        );
        cmd.Options.Add(labelOpt);
        cmd.Options.Add(scopeOpt);
        cmd.Options.Add(expiresOpt);
        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var label = parseResult.GetValue(labelOpt) ?? string.Empty;
                var scopes = parseResult.GetValue(scopeOpt) ?? Array.Empty<string>();
                var expiresRaw = parseResult.GetValue(expiresOpt);
                DateTimeOffset? expiresAt = null;
                if (!string.IsNullOrWhiteSpace(expiresRaw))
                {
                    var parsed = ParseExpiresAt(expiresRaw);
                    if (parsed is null)
                    {
                        Console.Error.WriteLine(
                            $"error: --expires '{expiresRaw}' is not a duration (30d/12h/5m) "
                                + "or an ISO-8601 instant."
                        );
                        return ExitCodeMapper.UsageError;
                    }
                    expiresAt = parsed;
                }
                var handler = services.GetRequiredService<CreateApiTokenCommandHandler>();
                var result = await handler.HandleAsync(
                    new CreateApiTokenCommand(
                        label,
                        Scopes: ImmutableArray.Create(scopes),
                        ExpiresAt: expiresAt
                    ),
                    ct
                );
                if (
                    result
                    is not Result<CreateApiTokenReport, ApiTokenStoreError>.Ok { Value: var report }
                )
                {
                    var err = ((Result<CreateApiTokenReport, ApiTokenStoreError>.Error)result).Err;
                    Console.Error.WriteLine($"error: token store failed: {err.Message}");
                    return ExitCodeMapper.ConfigurationError;
                }
                var displayLabel = string.IsNullOrEmpty(report.Stored.Label)
                    ? "(no label)"
                    : $"'{report.Stored.Label}'";
                Console.WriteLine($"created token {displayLabel} (id {report.Stored.Id})");
                if (!report.Stored.Scopes.IsDefaultOrEmpty)
                {
                    Console.WriteLine($"scopes: {string.Join(", ", report.Stored.Scopes)}");
                }
                if (report.Stored.ExpiresAt is { } exp)
                {
                    Console.WriteLine(
                        $"expires: {exp.ToString("u", CultureInfo.InvariantCulture)}"
                    );
                }
                Console.WriteLine(report.Token);
                Console.WriteLine();
                Console.WriteLine("Save it now — it cannot be recovered later. Hash is stored;");
                Console.WriteLine("the original token is not.");
                return ExitCodeMapper.Success;
            }
        );
        return cmd;
    }

    /// <summary>
    /// Parses an <c>--expires</c> argument as either a relative duration
    /// (<c>30d</c>, <c>12h</c>, <c>5m</c>, <c>120s</c>) added to the
    /// current UTC time, or an absolute ISO-8601 instant. Returns null
    /// when the input matches neither shape.
    /// </summary>
    public static DateTimeOffset? ParseExpiresAt(string raw)
    {
        raw = raw.Trim();
        if (raw.Length >= 2)
        {
            var unit = raw[^1];
            var nPart = raw[..^1];
            if (
                (unit == 's' || unit == 'm' || unit == 'h' || unit == 'd')
                && int.TryParse(
                    nPart,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var n
                )
                && n > 0
            )
            {
                var now = DateTimeOffset.UtcNow;
                return unit switch
                {
                    's' => now.AddSeconds(n),
                    'm' => now.AddMinutes(n),
                    'h' => now.AddHours(n),
                    'd' => now.AddDays(n),
                    _ => null,
                };
            }
        }
        if (
            DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var abs
            )
        )
        {
            return abs;
        }
        return null;
    }

    private static Command BuildList(IServiceProvider services)
    {
        var jsonOpt = new Option<bool>("--json")
        {
            Description = "Emit the listing as JSON instead of a table.",
        };
        var cmd = new Command("list", "List stored API tokens (hashes + metadata).");
        cmd.Options.Add(jsonOpt);
        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var json = parseResult.GetValue(jsonOpt);
                var handler = services.GetRequiredService<ListApiTokensQueryHandler>();
                var result = await handler.HandleAsync(new ListApiTokensQuery(), ct);
                if (
                    result
                    is not Result<ImmutableArray<ApiToken>, ApiTokenStoreError>.Ok
                    {
                        Value: var tokens,
                    }
                )
                {
                    var err = (
                        (Result<ImmutableArray<ApiToken>, ApiTokenStoreError>.Error)result
                    ).Err;
                    Console.Error.WriteLine($"error: token store failed: {err.Message}");
                    return ExitCodeMapper.ConfigurationError;
                }
                if (json)
                {
                    Console.WriteLine(RenderJson(tokens));
                }
                else
                {
                    RenderTable(tokens, Console.Out);
                }
                return ExitCodeMapper.Success;
            }
        );
        return cmd;
    }

    private static Command BuildRevoke(IServiceProvider services)
    {
        var idArg = new Argument<string>("id")
        {
            Description = "Token id (from `api token list`).",
        };
        var cmd = new Command("revoke", "Remove the token with the supplied id.");
        cmd.Arguments.Add(idArg);
        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var id = parseResult.GetRequiredValue(idArg);
                var handler = services.GetRequiredService<RevokeApiTokenCommandHandler>();
                var result = await handler.HandleAsync(new RevokeApiTokenCommand(id), ct);
                return result switch
                {
                    Result<ApiToken, RevokeApiTokenError>.Ok ok => RevokeOk(ok.Value),
                    Result<ApiToken, RevokeApiTokenError>.Error err => RevokeFail(err.Err),
                    _ => ExitCodeMapper.GenericFailure,
                };
            }
        );
        return cmd;
    }

    /// <summary>Renders the token table for plain text output.</summary>
    public static void RenderTable(ImmutableArray<ApiToken> tokens, TextWriter writer)
    {
        if (tokens.IsDefaultOrEmpty)
        {
            writer.WriteLine("(no API tokens)");
            return;
        }
        writer.WriteLine(
            $"{"ID", -8} {"LABEL", -20} {"CREATED", -22} {"EXPIRES", -22} {"SCOPES", -28} LAST USED"
        );
        foreach (var t in tokens)
        {
            var lastUsed = t.LastUsedAt?.ToString("u", CultureInfo.InvariantCulture) ?? "(never)";
            var label = string.IsNullOrEmpty(t.Label) ? "(no label)" : t.Label;
            var expires = t.ExpiresAt?.ToString("u", CultureInfo.InvariantCulture) ?? "(never)";
            var scopes = t.Scopes.IsDefaultOrEmpty ? "(unrestricted)" : string.Join(",", t.Scopes);
            writer.WriteLine(
                $"{t.Id, -8} {Truncate(label, 20), -20} {t.CreatedAt.ToString("u", CultureInfo.InvariantCulture), -22} {expires, -22} {Truncate(scopes, 28), -28} {lastUsed}"
            );
        }
    }

    /// <summary>Renders the token list as a JSON array.</summary>
    public static string RenderJson(ImmutableArray<ApiToken> tokens) =>
        JsonSerializer.Serialize(
            tokens
                .Select(t => new ApiTokenView(
                    t.Id,
                    t.Label,
                    t.CreatedAt,
                    t.LastUsedAt,
                    t.Scopes.IsDefaultOrEmpty ? Array.Empty<string>() : t.Scopes.ToArray(),
                    t.ExpiresAt
                ))
                .ToArray(),
            CliJsonContext.Default.ApiTokenViewArray
        );

    private static int RevokeOk(ApiToken token)
    {
        var label = string.IsNullOrEmpty(token.Label) ? "(no label)" : $"'{token.Label}'";
        Console.WriteLine($"revoked {label} (id {token.Id}).");
        return ExitCodeMapper.Success;
    }

    private static int RevokeFail(RevokeApiTokenError error)
    {
        Console.Error.WriteLine(
            error switch
            {
                RevokeApiTokenUnknown u => $"error: no API token with id '{u.Id}'.",
                RevokeApiTokenStoreFailure s => $"error: token store failed: {s.Inner.Message}",
                _ => "error: revoke failed.",
            }
        );
        return error switch
        {
            RevokeApiTokenUnknown => ExitCodeMapper.DeviceError,
            RevokeApiTokenStoreFailure => ExitCodeMapper.ConfigurationError,
            _ => ExitCodeMapper.GenericFailure,
        };
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : string.Concat(s.AsSpan(0, max - 1), "…");
}
