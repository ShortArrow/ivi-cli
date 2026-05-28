# 0044. PAT scopes + token expiry

- Status: Accepted
- Date: 2026-05-28

## Context

[ADR 0036](0036-management-api-authentication.md) shipped PAT tokens
without scopes or expiry — every minted token granted every route
forever. That stance was the right Tidy-First v1 (ship one
secure-enough mechanism, defer fine-grained policy), but ADR 0036
§Out-of-scope explicitly named both gaps:

> - **Token expiry / rotation policy** — v1 tokens live until
>   manually revoked; v2 adds `expiresAt`.
> - **Scopes / per-route permissions** — v1 = every valid token
>   reaches every route.

Operators running ivi-cli through CI / lab-automation pipelines
have been asking for short-lived tokens + read-only access for
dashboards. This ADR closes both gaps.

## Decision

### 1. Token-level fields

`ApiToken` gains two optional positional record parameters:

```csharp
public sealed record ApiToken(
    string Id,
    string HashHex,
    string Label,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    ImmutableArray<string> Scopes = default,   // ADR 0044
    DateTimeOffset? ExpiresAt = null            // ADR 0044
);
```

Both default to "unrestricted" / "no expiry" so existing call sites
and persisted tokens keep working without explicit values.

`ApiToken.HasScope(required)` returns true when `Scopes` is empty
(backward compatibility: tokens minted before ADR 0044 grant every
scope) or when `required` is in the list.

`ApiToken.IsExpired(now)` returns true when `ExpiresAt` is non-null
and `now > ExpiresAt`. Equal timestamps are treated as not yet
expired — matches typical JWT `exp` semantics.

### 2. Scope catalog

Four v1 scopes, all `verb:noun` shape so future additions stay
predictable:

| Scope | Permits |
| --- | --- |
| `read:devices` | `GET /v1/devices`, `GET /v1/devices/{name}/status` |
| `read:servers` | `GET /v1/servers` |
| `read:scenarios` | `GET /v1/scenarios` |
| `write:scpi` | `POST /v1/devices/{name}/{query,write}`, WebSocket `/v1/devices/{name}/ws` |

The mapping lives in `RoutePermissions.RequiredScope(method, path)`
as a small static table. Future API surface additions register
themselves there.

Unmapped routes (`/healthz`, `/openapi/v1.json`, future bypass
paths) return `null` from `RequiredScope` and skip the scope gate.

### 3. Auth middleware enforcement

`ApiTokenAuthentication` adds two checks after the existing PAT
hash match:

1. `IsExpired(UtcNow)` → audit `AuthFailed("expired_token")` + 401
   with body `{"error":{"code":"unauthorized","message":"API token has expired."}}`.
2. `RoutePermissions.RequiredScope` ≠ null and `HasScope` returns
   false → audit `AuthFailed("insufficient_scope")` + 403 with body
   `{"error":{"code":"insufficient_scope","message":"token does not have the '<scope>' scope."}}`.

403 (not 401) for scope failures so clients can tell "your token
is real but you don't have permission" from "your token is
invalid."

ADR 0043 audit log gains two new `AuthFailed.Reason` values
(`expired_token`, `insufficient_scope`) on top of the existing
`missing_token` / `invalid_token` / `no_tokens_configured` /
`token_store_unavailable`.

### 4. Persistence

TOML round-trip in `TomlApiTokenStore`:

```toml
[[token]]
id = "abc123"
hash = "<sha256 hex>"
label = "lab-dashboard"
createdAt = "2026-05-28T..."
scopes = ["read:devices", "read:servers"]
expiresAt = "2026-12-31T23:59:59+00:00"
```

Both new keys are optional. Loading a legacy `[[token]]` entry
without them yields `Scopes = default` + `ExpiresAt = null`, which
behaves as "unrestricted, never expires" per §1.

### 5. CLI surface

`ivicli api token create`:

```
--label <string>           existing
--scope <name>             new (repeatable; e.g. --scope read:devices)
--expires <duration|iso>   new
```

`--expires` accepts:
- Relative shortcuts: `30s` / `5m` / `12h` / `30d` — added to UTC now.
- ISO-8601 absolute instants: `2027-01-01T00:00:00Z`.
- Malformed input → `UsageError` exit code with a clean message.

`token list` (table + JSON) gains `SCOPES` and `EXPIRES` columns.
`(unrestricted)` and `(never)` render for legacy tokens.

`create` output prints the resolved scopes + expiry lines so the
operator sees exactly what was minted before saving the raw token.

### 6. Backward compatibility

- Existing minted tokens with no `scopes` / `expiresAt` in the
  TOML keep working unchanged — they map to legacy unrestricted
  behaviour in §1.
- Existing code paths constructing `ApiToken` without the new
  fields compile against the default record parameter values.
- Existing CLI invocations (`ivicli api token create --label X`)
  continue to mint legacy unrestricted tokens.

Operators tightening security explicitly re-mint with scopes /
expiry; ivi-cli does not retroactively migrate.

### 7. Out of scope (v1)

- **Refresh tokens / rotation flow** — operators rotate by
  minting a fresh token + revoking the old one. JWT-style
  refresh tokens add complexity (refresh endpoint, replay
  protection) that exceeds the lab-network audience.
- **Per-token rate limits** — covered (or not) by ADR 0017 §6
  rate-limiting row, deferred there.
- **Wildcard scopes** (`read:*`, `*:devices`) — every scope is
  literal in v1; future addition is additive.
- **Hierarchy / inheritance** between scopes — `write:scpi` does
  not imply `read:devices` automatically. Operators pass both
  explicitly.
- **Per-device or per-route ACLs** — ADR 0044 is about *what
  ops* (read, write); refining to *which device* needs a
  device-name-aware scope syntax (e.g. `write:scpi/psu*`),
  deferred.
- **Auto-expiry of unused tokens** — `LastUsedAt` already exists
  (ADR 0036) but does not trigger eviction.

## Consequences

- **Pipeline-friendly short-lived tokens** — CI minted a token
  with `--expires 6h --scope write:scpi`, expires automatically,
  no manual revoke step. Aligns with SOC2 short-lived-credential
  expectations.
- **Dashboard read-only access** — Grafana / status pages mint
  `--scope read:devices --scope read:scenarios` and cannot
  accidentally drive instruments.
- **Audit reasons grow** — ADR 0043 dashboards now distinguish
  `expired_token` vs `invalid_token` vs `insufficient_scope`
  vs `missing_token`. Useful for both ops monitoring and
  intrusion-detection signals.
- **No security regression by default** — legacy tokens keep
  working; operators have to actively opt in to tighter
  policies.

## Verification

- `dotnet test --filter "Category!=Integration"` covers:
  - Domain: `HasScope` empty-grants-all + populated-list, `IsExpired`
    null / past / equal / future.
  - Infrastructure: TOML round-trip of scopes + expiresAt, legacy
    token without either field.
  - Application: `CreateApiTokenCommand` threads scopes + expiry
    through the handler onto the persisted record.
  - API: expired token → 401 with `expired_token` audit; scoped
    token missing required scope → 403 with `insufficient_scope`
    audit; scoped token with matching scope → 200; legacy
    unrestricted token → passes every gate.
  - CLI: `ParseExpiresAt` duration shortcuts + ISO-8601 + rejection
    of empty / garbage / zero / negative / unknown-unit input.
- Manual: `ivicli api token create --scope read:devices --expires 7d`,
  inspect `api-tokens.toml` for `scopes = ["read:devices"]` and
  `expiresAt = "2026-06-04T..."`; attempt `POST /v1/devices/x/query`
  with the token, observe 403 + `insufficient_scope` audit event.
