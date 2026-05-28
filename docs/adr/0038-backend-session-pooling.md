# 0038. Backend session pooling

- Status: Accepted
- Date: 2026-05-28

## Context

ADRs 0010 §4 (DI) and 0014 (errors) established the
`IBackendFactory → IIviBackend` port + adapter shape. Every command
handler that needs an instrument session opens it at the start of
the call, runs its op, and closes the session in the finally block.
Three follow-on batches surfaced the cost of that contract:

- **Phase 3** (Batch G — `visa script` / `visa monitor`) chains
  many short SCPI ops; every chained command pays an open+close
  handshake even though it just talked to the same instrument
  500 µs ago.
- **Management API** (Batches I/J/K) accepts HTTP and WebSocket
  query/write traffic; each HTTP `POST /devices/{name}/query` opens
  a fresh wire session.
- **Concurrent gateway connections** to the same `Device` race on
  the underlying instrument today — no serialisation, no VISA-spec
  compliance for "one op at a time."

Pooling sessions across logical caller open/close cycles addresses
all three. Batch L's protocol depth landed cancellation-correct
sessions, so the pool can defer closes without leaving the abort
path in a half-baked state.

## Decision

### 1. Placement: `IBackendFactory` decorator

A new `PoolingBackendFactory : IBackendFactory, IAsyncDisposable`
lives in `IviCli.Application.Backends`. It wraps an inner
`IBackendFactory` and returns a `PoolingBackendProxy` from
`CreateFor(device)`. The proxy delegates `Write/Query/Read` to the
pooled inner backend; `Open` becomes a lease acquisition, `Close`
becomes a release. Handlers and CLI verbs are unchanged.

The composition root (`Program.cs`) reads `[pool]` from
`ConfigDocument` and inserts the pool layer between
`DefaultBackendFactory` and `CapturingBackendFactory` (the latter
keeps its outer position — see §5).

### 2. Key: `DeviceName`

Pool entries are keyed by `DeviceName`. Two different device names
that resolve to the same `VisaResource` get two pool entries (the
operator deliberately distinguished them in config). One pool entry
per device → at most one wire session per device.

This trades a small amount of resource-sharing (no automatic
connection reuse across two names for the same physical
instrument) for predictable per-device semantics. Operators who
want shared physical sessions register the same device name once.

### 3. Concurrency: cap = 1 per device

VISA sessions are not thread-safe per IVI spec (a write followed by
a concurrent write on the same session is UB; instruments behave
unpredictably). The pool enforces this at the layer above the
backend: each `PoolEntry` carries a `SemaphoreSlim(1, 1)`, and
`LeaseAsync` waits on it up to `device.Timeout`. Timeout returns
`PoolWaitTimeout(device, waited)` — distinct from
`TransportTimeout` (wire silent) or `TransportDisconnected` (wire
dead).

**Behaviour change for gateway servers**: today two concurrent
gateway connections to the same `Device` execute SCPI on it in
parallel; the underlying instrument either races or returns
undefined responses. Post-pool, the second connection's call
serialises behind the first. This is a correctness fix, not a
regression — but it is observable and is called out here.

Operators who need throughput against one physical instrument
register multiple `Device` rows pointing to the same VISA resource
under different names; the routing layer already supports this.

### 4. Eviction: idle + LRU

Two simultaneous limits:

- **Idle timeout** (default `60s`, configurable): a leased-and-
  released entry survives this long before the pool closes the
  underlying backend session. Background `ITimer` fires at
  `idle_timeout / 2` and sweeps; every `LeaseAsync` also runs a
  lazy sweep.
- **MaxDevices** (default `16`, configurable; `0` = unlimited):
  before inserting a new entry, the pool LRU-evicts the
  least-recently-used **idle** entry. Currently-leased entries are
  not eligible — under saturation the pool may temporarily exceed
  the cap until a release frees something. (The alternative —
  block the new entry until something is idle — would deadlock
  callers waiting on the same instrument.)

`max_session_age` (force-evict regardless of activity) is **not in
v1**; it can land in v2 if operators see long-lived state
corruption.

### 5. Decorator stack: `Capture(Pool(Default))`

Capture stays outermost so the NDJSON audit trail records the
**caller's intent** — every logical Open / Close issued from a
handler. The pool elides actual wire opens beneath that layer; the
audit trail is unchanged.

The reverse order (`Pool(Capture(Default))`) would record only the
real wire opens — operators reading their capture log would see
"the API claims it sent 100 queries; why is the log showing 1
open and 100 queries with no closes between?" That ambiguity is
worse than the small redundancy of an Open event the pool elided
beneath.

### 6. Broken-session detection: lazy + no retry

The pool does not pre-flight ping sessions or run heartbeats. Any
op (`Write`, `Query`, `Read`) that returns a `BackendError` marks
the proxy's lease as broken; the matching `Release` evicts the
entry and asynchronously closes the inner backend instead of
returning the entry to the pool.

**No automatic re-open + retry.** SCPI has side-effecting commands
(`SOURce:VOLTage 5`, `OUTPut ON`); a silent retry of a half-applied
op risks doubling outputs on the instrument. The caller (CLI verb,
script handler, HTTP endpoint) sees the same `BackendError` it
would have seen without the pool; retry policy stays with the
caller.

### 7. Lifecycle: default-on + IAsyncDisposable

`[pool] enabled = true` is the default. Operators can force the
historic behaviour with `enabled = false`; the composition root
branches and skips inserting the pool layer entirely. The pool
implements `IAsyncDisposable` so the DI container's scope dispose
calls `CloseAsync(...)` on every cached entry — process exit
doesn't leak sessions.

The background sweep timer is anchored to the pool's
`TimeProvider`. Production uses `TimeProvider.System`; tests inject
`FakeTimeProvider` (Microsoft.Extensions.TimeProvider.Testing
9.7.0) to advance idle deadlines synchronously.

### 8. Configuration

```toml
[pool]
enabled = true            # default
idle_timeout = "60s"      # accepts "ms" / "s" / "m" / "h" suffix
max_devices = 16          # 0 = unlimited
```

Validation lives on `PoolConfig.From(...)`; negative values surface
as `NegativeIdleTimeout` / `NegativeMaxDevices` (subtypes of
`PoolConfigError`).

## Consequences

- **Faster repeated traffic.** CLI scripts, monitor loops, the
  Management API, and gateway servers that hold idle sessions
  briefly all stop paying the open/close tax.
- **Stricter concurrency semantics.** Per-device cap=1 matches the
  VISA spec; some operators may need to adjust scripts that
  inadvertently relied on the prior race.
- **Operator-visible config.** A new `[pool]` table joins
  `[defaults]` / `[[devices]]` / `[[servers]]` / `[[routes]]` in
  `config.toml`.
- **New BackendError variant.** `PoolWaitTimeout` is added to the
  sum so handlers and the Management API can surface "queued
  behind another op" distinctly from wire failures.
- **Test stack grows.** `Microsoft.Extensions.TimeProvider.Testing`
  joins the central package manifest; only `IviCli.Application.Tests`
  references it.

## Out of scope (v1)

- `max_session_age` force-eviction.
- Pre-flight session ping (`*IDN?` validation) and heartbeat
  timers.
- `cap > 1` per device (multiple sessions to one instrument). The
  per-device routing pattern already covers this without protocol
  ambiguity.
- CLI flag overrides (`--pool-idle`, `--pool-disabled`). Config-
  file only for v1.
- ~~Pool-aware metrics emission~~ — landed in
  [ADR 0040](0040-opentelemetry-exporter.md): `ivi.pool.evictions`,
  `ivi.pool.lease_wait_timeouts`, and `ivi.pool.cached_entries`
  flow through the OTel pipeline when `[telemetry] enabled = true`.
- Per-backend custom pool policies (e.g. "never close idle Local
  sessions"). v1 has one global policy.
- Migration of the existing inline `TimeProvider.System` references
  in `MonitorDeviceCommand` / `StartServerCommand` to the new
  registered singleton. A separate Tidy-First pass.
