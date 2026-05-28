using System.Collections.Immutable;

namespace IviCli.Domain.Auth;

/// <summary>
/// A persisted Management API access token (ADR 0036 + ADR 0044).
/// The raw token is never stored — only its lowercase-hex SHA-256 hash.
/// <see cref="Id"/> is the first 6 hex chars of the hash and serves as
/// the operator-facing stable handle for
/// <c>ivicli api token list / revoke</c>. ADR 0044 adds
/// <see cref="Scopes"/> and <see cref="ExpiresAt"/>; empty scopes is
/// backward-compatible "all scopes" and a null expiry never expires.
/// </summary>
public sealed record ApiToken(
    string Id,
    string HashHex,
    string Label,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    ImmutableArray<string> Scopes = default,
    DateTimeOffset? ExpiresAt = null
)
{
    /// <summary>
    /// True when this token's <see cref="Scopes"/> list satisfies
    /// <paramref name="required"/>. An empty <see cref="Scopes"/>
    /// counts as "all scopes granted" for backward compatibility
    /// with tokens minted before ADR 0044 — operators tightening
    /// security re-mint with explicit scopes.
    /// </summary>
    public bool HasScope(string required)
    {
        if (Scopes.IsDefaultOrEmpty)
        {
            return true;
        }
        foreach (var s in Scopes)
        {
            if (string.Equals(s, required, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="now"/> is strictly past
    /// <see cref="ExpiresAt"/>. A null <see cref="ExpiresAt"/>
    /// never expires.
    /// </summary>
    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } exp && now > exp;
}

/// <summary>
/// All API tokens currently honoured by the Management API. The file
/// at <c>{auth-dir}/api-tokens.toml</c> serialises this aggregate root.
/// Mutating helpers return new instances so the aggregate stays
/// immutable per ADR 0023.
/// </summary>
public sealed record ApiTokenDocument(ImmutableArray<ApiToken> Tokens)
{
    /// <summary>The empty document used when the file has never been written.</summary>
    public static readonly ApiTokenDocument Empty = new(ImmutableArray<ApiToken>.Empty);

    /// <summary>Returns a new document with <paramref name="token"/> appended.</summary>
    public ApiTokenDocument Add(ApiToken token) => this with { Tokens = Tokens.Add(token) };

    /// <summary>Returns a new document without the token whose id matches.</summary>
    public ApiTokenDocument? Remove(string id)
    {
        var idx = -1;
        for (var i = 0; i < Tokens.Length; i++)
        {
            if (string.Equals(Tokens[i].Id, id, StringComparison.OrdinalIgnoreCase))
            {
                idx = i;
                break;
            }
        }
        if (idx < 0)
        {
            return null;
        }
        return this with { Tokens = Tokens.RemoveAt(idx) };
    }

    /// <summary>Looks up a token by its hex SHA-256 hash.</summary>
    public ApiToken? FindByHash(string hashHex)
    {
        foreach (var t in Tokens)
        {
            if (string.Equals(t.HashHex, hashHex, StringComparison.OrdinalIgnoreCase))
            {
                return t;
            }
        }
        return null;
    }

    /// <summary>
    /// Returns a new document where the token with <paramref name="id"/>
    /// has its <see cref="ApiToken.LastUsedAt"/> updated. Returns this
    /// instance unchanged when the id is unknown.
    /// </summary>
    public ApiTokenDocument TouchLastUsed(string id, DateTimeOffset when)
    {
        for (var i = 0; i < Tokens.Length; i++)
        {
            if (string.Equals(Tokens[i].Id, id, StringComparison.OrdinalIgnoreCase))
            {
                var updated = Tokens[i] with { LastUsedAt = when };
                return this with { Tokens = Tokens.SetItem(i, updated) };
            }
        }
        return this;
    }
}
