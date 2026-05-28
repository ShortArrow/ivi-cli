# 0043. Append-only audit log

- Status: Accepted
- Date: 2026-05-28

## Context

ADR 0017 §6 listed an audit log as a Phase 2 baseline expectation —
"events include server start/stop, route add/remove, authentication
success/failure, gateway client connect/disconnect." That row has
been the last unbuilt Security Boundaries item since PAT auth
(ADR 0036), TLS / mTLS (ADR 0039), and the OpenTelemetry exporter
(ADR 0040) all landed.

What the existing observability layers don't cover for security:

- **Serilog** records developer-facing log lines with mutable
  rolling-file semantics; they are designed to be readable, not to
  be cryptographically auditable.
- **CapturingBackend** records SCPI traffic for replay (ADR 0031),
  not auth events.
- **OpenTelemetry traces** flow to an external collector; an
  operator without that collector configured has no record of who
  authenticated when.

This ADR adds a dedicated append-only audit stream that lives on the
host filesystem regardless of the other observability stack.

## Decision

### 1. Port

```csharp
public interface IAuditLog
{
    Task AppendAsync(AuditEvent ev, CancellationToken ct);
}
```

A single method. Implementations are expected to be append-only —
no `Truncate`, no `Delete`. Operators who want rotation use the OS
toolchain (`logrotate`, Windows Task Scheduler) against the file.

### 2. Event sum

`AuditEvent` is an abstract record with these v1 variants:

| Variant | Kind | Fields beyond Timestamp |
| --- | --- | --- |
| `AuthSucceeded` | `auth.succeeded` | Mechanism, Subject, Transport |
| `AuthFailed` | `auth.failed` | Mechanism, Reason, Transport |
| `ConfigMutated` | `config.mutated` | Operation, Target |
| `ApiRequest` | `api.request` | Method, Path, Status, Subject?, LatencyMs |
| `ServerLifecycle` | `server.lifecycle` | Server, Action |

Mechanism is e.g. `"pat"` / `"mtls"` / `"anonymous"`. Reason for
`AuthFailed` is a stable identifier (`"missing_token"`,
`"invalid_token"`, `"no_tokens_configured"`,
`"token_store_unavailable"`) so dashboards can group by cause.
Transport distinguishes HTTP and WebSocket because the operator may
want different alerting on each.

`ApiRequest`'s `Subject` is nullable for unauthenticated paths
(`/healthz`, `/openapi/v1.json`) where the request middleware
runs but the auth middleware never ran.

### 3. Sink: NDJSON

`NdjsonAuditLog` (Infrastructure) writes one JSON object per line.
Each variant is serialised as a flat object — `kind` + `timestamp`
+ the variant's fields — so a `jq` filter can match on `kind`
without traversing a wrapper. Append mode with shared-read on the
file handle so `tail -f` works cross-platform. A per-instance
`SemaphoreSlim` serialises concurrent writers (the audit middleware,
auth middleware, future config mutators, gateway lifecycle).

The file lives at `${IVICLI_DATA_DIR}/audit/audit.ndjson` by default
(via `IviPaths.ResolveAuditDirectory` — Windows
LocalApplicationData, Linux/macOS `$XDG_CONFIG_HOME/ivi-cli/audit`).
Override via `[audit].path` in `config.toml` or the
`IVICLI_AUDIT_DIR` env var (same pattern as ADR 0036 §6).

### 4. Configuration

```toml
[audit]
enabled = true            # default
path    = "/var/log/ivi/audit.ndjson"   # optional override
```

Default-on. Operators who explicitly want no audit log set
`enabled = false` — the composition root binds `NullAuditLog` and
no IO happens. Whitespace-only `path` is rejected
(`AuditPathBlank`) to catch operator typos that would otherwise
default-silently to the canonical location.

### 5. Wiring

**Composition root** (`Program.cs`): the eagerly-loaded
`ConfigDocument.Audit` decides between `NdjsonAuditLog` and
`NullAuditLog`. Same pattern as ADR 0040 telemetry.

**Auth middleware** (`ApiTokenAuthentication`): emits
`AuthSucceeded(mechanism, subject, transport)` on success and
`AuthFailed(mechanism, reason, transport)` on every rejection
branch. Empty-store + anonymous opt-in counts as
`AuthSucceeded("anonymous", "(loopback)")` so an operator can see
exactly how often the loopback-anonymous path was used.

**API request middleware** (`IviCliApiBuilder`): outer-most so it
runs for both authorised and rejected requests. One
`ApiRequest(method, path, status, subject=null, latency_ms)` per
request. Audit failures inside the middleware are caught and
swallowed so an unwritable audit log never breaks the user
request.

**Gateway lifecycle / config mutations**: out of scope for this
initial wiring batch. The port + events are defined; concrete
gateway / handler-side emissions accrete in follow-up commits.

### 6. Privacy

- The audit log records token **labels** ("production",
  "lab-dashboard") and `Subject` strings — never the raw token
  bytes or the hash. An operator reviewing the log learns which
  PAT was used, not the secret value.
- SCPI payloads are NOT recorded here; capture (ADR 0031) is the
  right place for that. The audit log answers "who" + "did what"
  at the call-site level.
- mTLS subject (cert CN) lands as `Subject` when ADR 0039's mTLS
  middleware is wired into the auth path. v1 records the PAT
  label.

### 7. Out of scope (v1)

- **Cryptographic chaining / signed entries.** A future v2 may
  hash-chain entries so tampering surfaces; v1 trusts the
  filesystem's audit-quality.
- **Remote sink.** OTel logs / Loki / Sentry shipping is a
  separate batch — operators who want centralised storage attach
  it via `logrotate`-piped scripts in the meantime.
- **Config-mutation emissions.** `ConfigMutated` is defined but
  the per-handler call sites aren't yet emitting. Follow-up.
- **Gateway-server emissions.** `ServerLifecycle` is defined; the
  HiSlip/VXI-11/Socket gateway `RunAsync` enter/exit emissions
  ship next.
- **Filesystem rotation / retention.** The CLI does not rotate
  the file — operators use the OS toolchain.

## Consequences

- **Security review trail.** SOC2 / lab-IT audit asks "who used
  PAT X on Tuesday" land on a deterministic NDJSON timeline.
- **No-cost off-path.** `[audit] enabled = false` binds
  `NullAuditLog`; emission sites still call `AppendAsync` but the
  no-op implementation is a single `Task.CompletedTask`.
- **Decorator ordering.** The audit middleware sits outside auth;
  ADR 0039 TLS sits outside the audit middleware (TLS is
  pre-routing). Order: `TLS → audit → auth → routing`.
- **One file per process.** Multiple `ivicli` invocations on the
  same host share the audit file via append + shared-read. Use
  external rotation if file size matters.

## Verification

- `dotnet test --filter "Category!=Integration"` includes:
  - Codec round-trip for `[audit]` TOML.
  - `NdjsonAuditLog` MockFileSystem tests (one-line-per-event,
    field flattening, parent-directory creation, concurrent
    append integrity).
  - End-to-end via `ApiTestHost`: `/healthz` emits one
    `ApiRequest`; invalid bearer emits `AuthFailed` +
    `ApiRequest(401)`; valid bearer emits `AuthSucceeded` +
    `ApiRequest(200)` with the matching PAT label.
- Manual: `ivicli api start --tls-self-signed`, perform a few
  requests, inspect `audit.ndjson` with `jq '. | select(.kind ==
  "auth.succeeded")'`.
