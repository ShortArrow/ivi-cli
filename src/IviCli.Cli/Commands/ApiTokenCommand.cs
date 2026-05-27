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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

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
        var cmd = new Command(
            "create",
            "Mint a new API token. The raw token is printed once and never recoverable."
        );
        cmd.Options.Add(labelOpt);
        cmd.SetAction(
            async (parseResult, ct) =>
            {
                var label = parseResult.GetValue(labelOpt) ?? string.Empty;
                var handler = services.GetRequiredService<CreateApiTokenCommandHandler>();
                var result = await handler.HandleAsync(new CreateApiTokenCommand(label), ct);
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
                Console.WriteLine(report.Token);
                Console.WriteLine();
                Console.WriteLine("Save it now — it cannot be recovered later. Hash is stored;");
                Console.WriteLine("the original token is not.");
                return ExitCodeMapper.Success;
            }
        );
        return cmd;
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
        writer.WriteLine($"{"ID", -8} {"LABEL", -20} {"CREATED", -25} LAST USED");
        foreach (var t in tokens)
        {
            var lastUsed = t.LastUsedAt?.ToString("u", CultureInfo.InvariantCulture) ?? "(never)";
            var label = string.IsNullOrEmpty(t.Label) ? "(no label)" : t.Label;
            writer.WriteLine(
                $"{t.Id, -8} {Truncate(label, 20), -20} {t.CreatedAt.ToString("u", CultureInfo.InvariantCulture), -25} {lastUsed}"
            );
        }
    }

    /// <summary>Renders the token list as a JSON array.</summary>
    public static string RenderJson(ImmutableArray<ApiToken> tokens) =>
        JsonSerializer.Serialize(
            tokens
                .Select(t => new
                {
                    id = t.Id,
                    label = t.Label,
                    createdAt = t.CreatedAt,
                    lastUsedAt = t.LastUsedAt,
                })
                .ToArray(),
            JsonOptions
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
