# 0036. Management API authentication (PAT)

- Status: Accepted
- Date: 2026-05-28

## Context

ADR 0034 §5 and ADR 0035 §5 both pinned the v1 stance — "no
authentication, loopback only" — as the cost of shipping the HTTP +
WebSocket transports without holding for an authn design. Operators
who needed lab-network access either ran a reverse proxy with its own
auth or accepted that any local user could drive the instruments.
This ADR retires that constraint with a PAT (Personal Access Token)
flow: tokens are minted by a CLI verb, persisted as SHA-256 hashes,
validated by a single middleware that gates both HTTP and WebSocket
entry points.

## Decision

### 1. Token format

```
ivicli_pat_<base64url(32 random bytes)>
```

- 32 bytes from `RandomNumberGenerator.GetBytes` → base64url (no
  padding, `-` and `_` replacements) → ~54-character total length.
- The `ivicli_pat_` prefix lets log scanners / GitHub secret-detection
  tooling recognise leaked tokens.
- Tokens are **shown to the operator exactly once** at creation time;
  only the SHA-256 hex of the raw token is persisted.

### 2. Storage

- File: `<auth-dir>/api-tokens.toml`. `auth-dir` resolves per OS via
  `IviPaths.ResolveAuthDirectory()`:
  - Linux: `$XDG_CONFIG_HOME/ivi-cli/auth/` (default
    `~/.config/ivi-cli/auth/`)
  - macOS: `~/.config/ivi-cli/auth/`
  - Windows: `%LOCALAPPDATA%\ivi-cli\auth\`
- Override: `IVICLI_AUTH_DIR` environment variable.
- Layout: one `[[token]]` table per entry with
  `id` / `hash` / `label` / `createdAt` / `lastUsedAt?`.
- Atomic write: `<path>.tmp` → move. Same pattern as
  `TomlScenarioStore`.

### 3. Bypass list

These paths always pass authentication (so reverse proxies and
health-check probes don't need to know about tokens):

- `GET /healthz`
- `GET /openapi/v1.json`

### 4. Non-loopback gate

The CLI `api start` verb refuses to bind a non-loopback address
unless one of:

- ≥ 1 token is configured (`ivicli api token create`).
- `--allow-anonymous` is passed (explicit opt-out).

The gate runs **before** the listener binds, so a misconfigured
operator gets a remediation message immediately instead of an
exposed listener.

### 5. Validation paths

| Transport | Where the token comes from |
| --- | --- |
| HTTP routes | `Authorization: Bearer <token>` header |
| WebSocket upgrade | `Sec-WebSocket-Protocol: ivi-cli-pat.<token>` (one of the requested sub-protocols) |

The WebSocket convention exists because browsers cannot send custom
HTTP headers on the WS handshake; carrying the token as a
sub-protocol is the standard browser-WS auth pattern. The server
**does not echo the token back** in the negotiated sub-protocol —
only the `ivi-cli-pat` prefix.

### 6. Comparison

Token hashes are compared with
`System.Security.Cryptography.CryptographicOperations.FixedTimeEquals`
against the lowercase-ASCII hex of the SHA-256 to eliminate timing
side-channels.

### 7. Last-used touch

On a successful match the middleware fires-and-forgets a `SaveAsync`
that updates `LastUsedAt` on the matching token. The request is never
blocked by the touch; if the save fails the operator simply sees a
stale value in `ivicli api token list`. v2 may add structured logging
for the failure.

### 8. Error envelope

401 responses match the ADR 0034 §3 shape:

```json
{ "error": { "code": "unauthorized", "message": "..." } }
```

Codes:

- `unauthorized` — generic missing / invalid / expired token (v1
  rolls them all into one to avoid an oracle).
- `token_store_unavailable` — the on-disk file could not be read.

## Out of scope (v2 candidates)

- **Token expiry / rotation policy** — v1 tokens live until manually
  revoked; v2 adds `expiresAt`.
- **Scopes / per-route permissions** (`read:devices`, `write:visa`).
  v1 = every valid token reaches every route.
- **TLS / mTLS** — landed in [ADR 0039](0039-management-api-tls.md).
  TLS is opt-in (`[api.tls] enabled = false` by default). PAT and
  mTLS compose: mTLS gates *who* can connect to the listener, PAT
  gates *what* they can do.
- **OIDC / SSO** federation — separate ADR.
- **Audit log of auth events** — Batch F's `IVICLI_CAPTURE` records
  SCPI traffic but not auth. v2 adds an `IAuditLog` port.
- **Rate limiting / brute-force protection** — v1 stance is
  loopback-default + opt-in non-loopback; v2 can layer in IP
  throttling once concrete deployments emerge.

## Consequences

- The non-loopback HTTP + WebSocket surface is safe to expose to the
  lab network as long as at least one token is configured.
- ADRs 0034 §5 and 0035 §5 update to point here for the active authn
  contract.
- No new package dependency; SHA-256 +
  `CryptographicOperations.FixedTimeEquals` are BCL-tier.
- The `ivi-cli-pat_` prefix is now a stable identifier that lets
  third-party secret scanners detect leaked tokens.
