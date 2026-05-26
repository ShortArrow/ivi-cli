# 0028. Replay Backend

- Status: Accepted
- Date: 2026-05-26

## Context

ADR 0026 introduced the Mock Scenario System and listed the
"author-from-scratch scenarios" flow as in-scope while explicitly
calling **recording from a real backend** out of scope. Phase 3
(`mock scenario record --from-script`) closed the recording half by
replaying a SCPI script against any backend and capturing each
query/write into the scenario file. The remaining half — **playing
back a recorded scenario as if it were a real instrument** — is
already 90 % covered because `FakeBackend.ActivateScenario` consults
the active `MockScenario` before its programmatic DSL.

The remaining gap is a contract one. `FakeBackend` provides three
fallback layers (active scenario → DSL → synthetic `*IDN?`), which
makes it ergonomic for tests but **wrong for "play the recording
exactly as captured"** use cases: a missing scene silently degrades to
the IDN default instead of telling the operator that the recording is
incomplete. PRD §5.3 named the alternative "ReplayBackend"; this ADR
turns that name into a concrete backend assembly.

## Decision

### 1. Scope

A new project `IviCli.Backends.Replay` ships a single
`IIviBackend` implementation called `ReplayBackend` that:

- Holds **exactly one** immutable `MockScenario`.
- Open / Close are no-ops.
- Write / Query / Read consult `MockScenario.FindByMatch`; no DSL,
  no synthetic responses.
- Scene miss → `ReplayMiss` (a `BackendError` variant). Action
  mismatch (e.g. `Respond` on a Write) → `ReplayActionMismatch`.
  Explicit `SceneAction.Fail` → `ReplayCannedFailure` preserving the
  variant string verbatim.

### 2. Activation: `IVICLI_REPLAY` environment variable

The CLI composition root in `Program.cs` checks
`IVICLI_REPLAY=<scenario-name>` at startup. When set:

1. Resolve `IScenarioStore` from DI.
2. Load the named scenario via `IScenarioStore.LoadAsync`.
3. Instantiate `ReplayBackend(scenario)`.
4. Hand it to `DefaultBackendFactory.fallbackBackend` (replacing the
   `FakeBackend` fallback).

The HiSLIP / SOCKET / Local TCPIP/USB/GPIB dispatch path is unchanged;
operators who want **every** device routed through replay simply
configure the device with a non-routed VISA resource so the factory
falls through to the fallback slot. A future "force-replay-all" knob
is not part of v1.

Invalid scenario name or missing scenario file are logged as
warnings and the CLI falls back to the existing FakeBackend (so a
misconfigured env var never crashes the binary).

### 3. Why not extend FakeBackend?

`FakeBackend` is the test-double anchor for the entire test suite. It
carries:

- A programmatic DSL (`ConfigureDevice`, `RespondToQuery`, etc.) used
  by handler-level unit tests.
- A `*IDN?` synthesizer that backs the "out-of-the-box ivicli works
  without a real instrument" UX claim in PRD §4.2.
- A fault-injection surface (`FailNextOpen`, etc.) consumed by
  connection-lifecycle tests (ADR 0009 §6).

Forcing scene-strict semantics onto `FakeBackend` would change all
three. Keeping them separate is the cheapest way to (a) preserve
existing tests verbatim, (b) make the playback contract loud and
explicit at the error layer, and (c) leave the door open for further
playback-specific features (loop, jump, conditional branching) without
polluting `FakeBackend`.

### 4. Domain extension: none

`ReplayBackend` consumes existing `Domain.Mock.MockScenario` and
`SceneAction` (Respond / Ack / Fail). No new Domain types. The
project sits at the same layer as the other Backends; the architecture
test suite (`DependencyDirectionTests`) is extended to cover it.

### 5. Error mapping at the CLI surface

The three new `BackendError` variants surface through the existing
`ExitCodeMapper` paths:

- `ReplayMiss` → usage-error exit code (the recording is incomplete or
  the wrong scenario is active).
- `ReplayActionMismatch` → usage-error.
- `ReplayCannedFailure` → transport-error (matches how
  `SceneAction.Fail` was already mapped by FakeBackend).

### 6. Out of scope

- VisaResource-level "replay" scheme (e.g. `REPLAY::demo`).
- Multi-scenario composition / overlays.
- Dynamic scenario reload while the CLI is running.
- Recording-while-replaying (round-trip enrichment).

These remain candidates for a follow-up ADR if usage data justifies
them.

## Consequences

**Pros**
- Closes the record → replay loop opened by ADR 0026 + ADR 0027.
- Activation flow (`IVICLI_REPLAY` env var) costs zero changes to
  existing commands or to `ConfigDocument`.
- Strict semantics surface incomplete recordings as hard errors,
  which is exactly what test-engineer users want.

**Cons**
- Two backends now consult `MockScenario` (Fake and Replay), with
  slightly different semantics. The trade-off is documented in §3.
- One more assembly + composition-root branch to keep in sync when
  the backend factory grows.

## References

- PRD §5.3 — ReplayBackend listed as future extension.
- ADR 0026 — Mock Scenario System (defines `MockScene` / `SceneAction`).
- ADR 0027 — Phase 3 operator automation (defines `mock scenario record`).
- ADR 0028 (this ADR).
