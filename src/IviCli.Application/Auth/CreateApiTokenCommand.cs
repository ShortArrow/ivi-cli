using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using IviCli.Domain;
using IviCli.Domain.Auth;

namespace IviCli.Application.Auth;

/// <summary>Command DTO for <c>ivicli api token create</c> (ADR 0036, scopes + expiry per ADR 0044).</summary>
/// <param name="Label">Human-readable label for the token (truncated to 64 chars).</param>
/// <param name="Scopes">Optional capability list. Empty = legacy unrestricted (all scopes granted).</param>
/// <param name="ExpiresAt">Optional absolute expiry. Null = never expires.</param>
public sealed record CreateApiTokenCommand(
    string Label,
    ImmutableArray<string> Scopes = default,
    DateTimeOffset? ExpiresAt = null
);

/// <summary>
/// Outcome of a successful mint. The raw token string is surfaced to
/// the CLI exactly once — only its hash is persisted.
/// </summary>
public sealed record CreateApiTokenReport(string Token, ApiToken Stored);

/// <summary>
/// Application handler that mints a new API token (ADR 0036). Random
/// 32 bytes → base64url → "ivicli_pat_" prefix; SHA-256 of the raw
/// string is stored alongside metadata. The raw token is never
/// persisted.
/// </summary>
public sealed class CreateApiTokenCommandHandler
{
    /// <summary>Token prefix used to recognise an ivi-cli PAT in plaintext.</summary>
    public const string TokenPrefix = "ivicli_pat_";

    private const int RandomByteLength = 32;
    private const int MaxLabelLength = 64;

    private readonly IApiTokenStore _store;
    private readonly Func<DateTimeOffset> _now;

    /// <summary>Creates a new handler. Pass a clock for tests; defaults to UTC now.</summary>
    public CreateApiTokenCommandHandler(IApiTokenStore store, Func<DateTimeOffset>? now = null)
    {
        _store = store;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Mints the token, persists the hash, and returns the raw + stored records.</summary>
    public async Task<Result<CreateApiTokenReport, ApiTokenStoreError>> HandleAsync(
        CreateApiTokenCommand command,
        CancellationToken ct
    )
    {
        var loaded = await _store.LoadAsync(ct);
        if (loaded is not Result<ApiTokenDocument, ApiTokenStoreError>.Ok { Value: var document })
        {
            return Result.Failure<CreateApiTokenReport, ApiTokenStoreError>(
                ((Result<ApiTokenDocument, ApiTokenStoreError>.Error)loaded).Err
            );
        }

        var rawBytes = RandomNumberGenerator.GetBytes(RandomByteLength);
        var token = TokenPrefix + Base64UrlEncode(rawBytes);
        var hashHex = HashHex(token);
        var id = hashHex[..6];
        var label = TrimLabel(command.Label);
        var stored = new ApiToken(
            id,
            hashHex,
            label,
            _now(),
            LastUsedAt: null,
            Scopes: command.Scopes.IsDefault ? ImmutableArray<string>.Empty : command.Scopes,
            ExpiresAt: command.ExpiresAt
        );

        var save = await _store.SaveAsync(document.Add(stored), ct);
        if (save is Result<Unit, ApiTokenStoreError>.Error err)
        {
            return Result.Failure<CreateApiTokenReport, ApiTokenStoreError>(err.Err);
        }
        return Result.Success<CreateApiTokenReport, ApiTokenStoreError>(
            new CreateApiTokenReport(token, stored)
        );
    }

    /// <summary>Hex-encoded SHA-256 of <paramref name="raw"/> (UTF-8 bytes).</summary>
    public static string HashHex(string raw)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(raw), hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string TrimLabel(string raw)
    {
        var trimmed = (raw ?? string.Empty).Trim();
        return trimmed.Length > MaxLabelLength ? trimmed[..MaxLabelLength] : trimmed;
    }
}
