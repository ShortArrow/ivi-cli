# 0031. VISA Traffic Capture

- Status: Accepted
- Date: 2026-05-27

## Context

PRD §15 listed "VISA traffic capture" as a Planned feature. Operators
running long campaigns want a chronological log of every SCPI exchange
— open / close / write / query / read — so they can audit a session
after the fact, diff today's run against yesterday's, or hand the file
to support without re-running the test. The data exists transiently
inside `IIviBackend` calls today; nothing persists it.

Mock-scenario record (ADR 0027) captures something similar, but with a
different goal: produce a *replayable* `MockScenario` scoped to one
script. Traffic capture covers **all** backend traffic, from any verb
(`visa query`, `visa watch`, `visa monitor`, the future Management
API), into an append-only NDJSON log. Distinct concept, distinct file
format, distinct activation.

## Decision

### 1. Wire-format

NDJSON (one JSON object per line, UTF-8, `\n` terminator):

```json
{"timestamp":"2026-05-27T12:00:00.123Z","device":"psu1","op":"Open","data":null,"response":null,"ok":true,"latencyMs":null,"error":null}
{"timestamp":"2026-05-27T12:00:00.150Z","device":"psu1","op":"Query","data":"*IDN?","response":"ACME,PSU,1,1.0","ok":true,"latencyMs":12,"error":null}
{"timestamp":"2026-05-27T12:00:00.165Z","device":"psu1","op":"Close","data":null,"response":null,"ok":true,"latencyMs":null,"error":null}
```

- `timestamp` — UTC, ISO-8601 with milliseconds.
- `device` — alias string.
- `op` — one of `Open` / `Close` / `Write` / `Query` / `Read`.
- `data` — SCPI request text for Write / Query; null for the others.
- `response` — backend response for Query / Read; null for the others.
- `ok` — true when the underlying backend call returned success.
- `latencyMs` — round-trip duration for Query / Read; null for the others.
- `error` — `BackendError.Message` when `ok` is false; null otherwise.

The schema is intentionally distinct from `MockScenario` (TOML, with
match/action shape). Converters between the two formats are deferred
to a future ADR.

### 2. Activation

Single environment variable, mirroring `IVICLI_REPLAY` (ADR 0028 §2):

```
IVICLI_CAPTURE=<path>
```

- Absolute path → used as-is.
- Relative path → resolved against `IviPaths.ResolveLogDirectory()`
  (the same directory that holds the Serilog rolling logs).
- Parent directory is created lazily on the first append.
- File is opened in `FileMode.Append + FileAccess.Write + FileShare.Read`
  so `tail -f` against it works on every supported platform.
- Unset → capture is disabled with zero overhead (the `NullTrafficWriter`
  singleton is bound by default in
  `InfrastructureServiceCollectionExtensions`).

A per-command `--capture <path>` flag is **out of scope** for v1;
deferred to a future revision if the env-var pathway proves
insufficient.

### 3. Layer placement

- Port `ITrafficWriter` + `TrafficEvent` record + `TrafficOp` enum live
  in `IviCli.Application.Capture` so the Application layer has no
  `System.IO` dependency.
- Adapter `NdjsonTrafficWriter` lives in `IviCli.Infrastructure.Capture`,
  uses the existing `IFileSystem` abstraction (TestableIO), and serialises
  concurrent writes via a `SemaphoreSlim` so verbs like `visa watch`
  (parallel `Task.WhenAll` probes) cannot interleave bytes.
- Decorator `CapturingBackend` + `CapturingBackendFactory` live in
  `IviCli.Application.Backends`. The factory wrapper is installed at the
  composition root (`src/IviCli.Cli/Program.cs`) immediately after
  `DefaultBackendFactory`, so every transport (HiSlip / Vxi11 / Local /
  Socket / Fake / Replay) participates without per-verb plumbing.

### 4. Failure semantics

- Sink failures (disk full, permission denied, …) are **swallowed**
  inside `CapturingBackend` and logged once at Warning. The operator's
  verb must never fail because the audit sink failed.
- `OperationCanceledException` from the sink **propagates** so the
  verb's cancellation flow stays intact.
- An invalid `IVICLI_CAPTURE` value at startup logs a Warning and
  falls back to `NullTrafficWriter`; the CLI continues without
  capture rather than refusing to start.

### 5. Read-side consumer — `mock received`

The capture is not only an audit trail; because the reader opens the
NDJSON with shared-read access, a *separate process* can query it while a
gateway is still writing. That is the substrate for confirming, out of
band, that a client's SCPI write reached a mock.

`ivicli mock received <device> [--match <substr>] [--all] [--json]` reads
the capture at `--capture <path>` (defaulting to `IVICLI_CAPTURE`),
filters to `Write` events for the device, and reports the matching SCPI —
the last write by default, or every match with `--all`. It exits non-zero
when nothing matched, so a test can assert "the write did (not) arrive"
without parsing stdout. The query lives in the Application layer
(`MockReceivedWritesQueryHandler`) over the existing `INdjsonTrafficReader`; no new
persistence path is introduced.

This is why a client-app integration test can drive the mock through its
own VISA stack (never sending raw SCPI itself) yet still verify the exact
bytes that reached the instrument.

## Out of scope (v2 candidates)

- **Per-command `--capture <path>` flag for *writing*.** The env var is
  enough to *enable* capture; `mock received --capture` only selects which
  log to *read*.
- **File rotation / size cap.** Operators rotate via `logrotate` or
  similar; a built-in cap can land in a follow-up ADR if real usage
  demands it.
- **Generic viewer / `ivicli capture tail` verb.** `tail -f` + `jq`
  covers ad-hoc inspection; only the focused, machine-readable
  `mock received` query (§5) is built, for the write-verification use case.
- **Format compatibility with `MockScenario`.** Different goals,
  different shapes; a converter is a separate concern.
- **Redaction filters** (drop secrets in SCPI text). Deferred until a
  concrete requirement surfaces.

## Consequences

- Every backend operation across the whole CLI gains an audit trail
  with one environment variable, including the Management API once
  that lands (PRD §7.5).
- Storage cost is operator-controlled (rotate the file or unset the
  env var when not needed).
- One new `Microsoft.Extensions.Logging.Abstractions` package
  reference on the Application layer to let `CapturingBackend` take
  an optional `ILogger<>` — this is the BCL-tier logging-abstractions
  package and does not violate the layered direction (no logging
  *implementation* enters Application).
- Future expansion paths (per-command flag, rotation, viewer) require
  only a new ADR + small additions; the layer boundaries set here are
  already correct.
