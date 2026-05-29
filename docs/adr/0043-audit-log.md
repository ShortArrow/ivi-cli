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
| `ConfigMutated` | `config.mutated` | Operation, Target, Subject? |
| `ApiRequest` | `api.request` | Method, Path, Status, Subject?, LatencyMs |
| `ServerLifecycle` | `server.lifecycle` | Server, Action, Subject? |

Mechanism is e.g. `"pat"` / `"mtls"` / `"anonymous"`. Reason for
`AuthFailed` is a stable identifier (`"missing_token"`,
`"invalid_token"`, `"no_tokens_configured"`,
`"token_store_unavailable"`) so dashboards can group by cause.
Transport distinguishes HTTP and WebSocket because the operator may
want different alerting on each.

`ApiRequest`'s `Subject` is nullable for unauthenticated paths
(`/healthz`, `/openapi/v1.json`) where the request middleware
runs but the auth middleware never ran.

`ConfigMutated.Subject` and `ServerLifecycle.Subject` are nullable
positional record parameters (Batch U) — added at the tail with a
`null` default so existing 3-arg construction stays source-
compatible. Production CLI invocations resolve the subject via
`IAuditSubject`, whose CLI impl returns
`$"cli/{Environment.UserName}"`. Tests substitute a fixed value.
A future Management-API-driven mutation path would add an
`HttpContextAuditSubject` impl returning `$"api/{token.Label}"`,
matching the same convention.

`ConfigMutated.Operation` follows a `{entity}.{verb}` shape
(`device.add`, `scene.remove`, `scenario.import`) so dashboards
can prefix-filter (`operation startsWith "scene."`). `Target` is
the entity primary key, slash-joined for nested children
(`scenario1/sceneA` for scenes, `server1/hislip0` for routes).

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

**Config mutations** (Batch U): every command handler that
persists operator-managed state emits one `ConfigMutated` on
successful save. Wired sites:

| Handler | Operation | Target |
| --- | --- | --- |
| `AddDeviceCommandHandler` | `device.add` | `{DeviceName}` |
| `RemoveDeviceCommandHandler` | `device.remove` | `{DeviceName}` |
| `AddServerCommandHandler` | `server.add` | `{ServerName}` |
| `RemoveServerCommandHandler` | `server.remove` | `{ServerName}` |
| `AddRouteCommandHandler` | `route.add` | `{ServerName}/{Endpoint}` |
| `RemoveRouteCommandHandler` | `route.remove` | `{ServerName}/{Endpoint}` |
| `AddSceneCommandHandler` | `scene.add` | `{ScenarioName}/{Match}` |
| `RemoveSceneCommandHandler` | `scene.remove` | `{ScenarioName}/{Index}` |
| `CreateScenarioCommandHandler` | `scenario.create` | `{ScenarioName}` |
| `RemoveScenarioCommandHandler` | `scenario.remove` | `{ScenarioName}` |
| `ImportScenarioFromTrafficCommandHandler` | `scenario.import` | `{ScenarioName}` |

Emission fires only on `SaveAsync` success — failed saves are
operational errors, not security events. The architecture test
`AuditWiringTests` scans `IviCli.Application` for command handlers
prefixed `Add` / `Remove` / `Create` / `Import` whose ctor depends
on `IConfigStore` or `IScenarioStore` and verifies each also
depends on `IAuditLog`. Drift guard for future handlers added
without audit injection.

**Gateway lifecycle** (Batch U): `StartServerCommandHandler` emits
`ServerLifecycle(server, "start", subject)` after the PID-registry
write and before `gateway.RunAsync`, then emits a terminal event
in the `finally` block:

- `gateway.RunAsync` returned `Result.Error` → `"crashed"`
- otherwise (success or cooperative cancellation) → `"stop"`

The terminal append uses `CancellationToken.None` so a cancelled
outer token does not skip the audit emission itself.
`StopServerCommand` (the SIGTERM-equivalent signal sender) does
not emit — the start-side handler's terminal event already
captures the actual transition timestamp.

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
- **Token CRUD audit.** `CreateApiToken` / `RevokeApiToken`
  (ADR 0036) persist to a separate store (`api-tokens.toml`);
  `ConfigMutated` deliberately does not cover them. A future
  `AuthAdmin(Action, TokenId, Subject)` variant covers that path
  if operator demand surfaces.
- **`SetCurrentDevice` audit.** Session-pointer changes are
  excluded as noise — operators care about config mutations, not
  CLI cursor moves.
- **`ConfigMutationFailed` variant.** Failed saves are operational
  errors logged elsewhere; security review needs the "what
  actually changed" timeline, not failed attempts.
- **Per-gateway-connection audit** (HiSlip / VXI-11 client
  connect/disconnect inside `gateway.RunAsync`). Out of v1 — the
  ADR 0017 §6 baseline is satisfied by the `ServerLifecycle`
  enter/exit pair plus existing OTel spans for per-request work.
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
    append integrity, Subject round-trip for both new variants).
  - End-to-end via `ApiTestHost`: `/healthz` emits one
    `ApiRequest`; invalid bearer emits `AuthFailed` +
    `ApiRequest(401)`; valid bearer emits `AuthSucceeded` +
    `ApiRequest(200)` with the matching PAT label.
  - `ConfigMutatedWiringTests` Theory body — 11 rows, one per
    mutating handler, asserts exactly one `ConfigMutated`
    emission with the expected `{entity}.{verb}` Operation /
    slash-joined Target / forwarded Subject.
  - `AuditWiringTests` Architecture guard — every
    `{Add,Remove,Create,Import}*CommandHandler` whose ctor
    depends on `IConfigStore` or `IScenarioStore` must also
    depend on `IAuditLog`. Future handlers added without audit
    wiring fail this test instead of silently swallowing.
  - `StartServerLifecycleAuditTests` — normal / failed /
    cancelled gateway termination paths assert the action
    sequence (`[start, stop]` / `[start, crashed]`) and the
    forwarded subject.
- Manual: `ivicli api start --tls-self-signed`, perform a few
  requests, inspect `audit.ndjson` with `jq '. | select(.kind ==
  "auth.succeeded")'`. For Batch U follow-up:
  `ivicli device add psu1 TCPIP::1.2.3.4::INSTR` then
  `jq 'select(.kind == "config.mutated")' audit.ndjson` shows
  `{operation:"device.add", target:"psu1", subject:"cli/<user>"}`.
