# 0026. Mock Scenario System

- Status: Accepted
- Date: 2026-05-22

## Context

PRD §5.3 lists `FakeBackend` as the in-memory Backend variant alongside
`ReplayBackend` (future), and ADR 0009 §6 describes a programmatic
fault-injection DSL on `FakeBackend` for handler / Cli tests. Neither
addresses a workflow that contributors and PRD §4 target users — test
automation engineers, embedded/FPGA developers — have repeatedly asked
for: **driving the CLI from the command line without real instruments**,
by pre-defining how a fake device should respond.

`ReplayBackend` (Phase 2) covers the "play back a recorded real session"
case. The remaining gap is **author-from-scratch scenarios** that the
user composes via the CLI itself — a curated set of canned responses
that the FakeBackend serves when no real Backend is configured.

This ADR introduces the *mock scenario system*: a set of files under the
state directory, a set of CLI commands to manage them, and a hook in
`FakeBackend` that consults the active scenario before falling back to
its existing programmatic DSL.

## Decision

### 1. Scope: FakeBackend only

The scenario system applies **only to `IviCli.Backends.Fake`**. Real
Backends (LocalVisaBackend, HiSlipBackend, SocketBackend, etc.) are
unaffected. This keeps the Backend abstraction clean — real transports
talk to real hardware; mocking lives where mocking belongs.

### 2. Persistence layout

Scenarios are stored as **one TOML file per scenario** under the state
directory:

```
$STATE_DIR/ivi-cli/scenarios/
 ├─ psu-startup.toml
 ├─ scope-noise.toml
 └─ ...
```

- The directory is created lazily on first write.
- File permissions follow ADR 0017 §4 (user-only on Unix, default ACL on
  Windows for now).
- One file per scenario keeps editing, diffing, and shell-completion
  natural; aggregate management lives in the CLI commands (§5).

The active-scenario pointer is persisted in `session.json` as a new
optional field `active_scenario` (string). The environment variable
`IVICLI_SCENARIO` takes precedence when set, so per-shell overrides
work without touching the session file.

### 3. Scenario schema (v1)

A scenario is a flat, *table-lookup* collection of scenes. No ordering,
no state machine — yet.

```toml
name = "psu-startup"
idn = "ACME,FAKE-PSU,001,1.0"           # optional default IDN

[[scenes]]
match = "*IDN?"                          # exact SCPI text to match
respond = "ACME,FAKE-PSU,001,1.0"        # textual response

[[scenes]]
match = "MEAS:VOLT?"
respond = "3.30"

[[scenes]]
match = "OUTP ON"
ack = true                               # write command, no response expected

[[scenes]]
match = "MEAS:CURR?"
fail = "transport_timeout"               # canned failure variant
fail_detail = "50"                       # variant-specific payload (e.g. ms)
```

- Exactly one of `respond` / `ack` / `fail` must be set per scene.
- `match` is matched **as exact strings** in v1. Regex / wildcard
  matching is deferred to a future revision.
- Scenes have no defined order. Lookup is O(scenes-in-scenario); a
  later revision can adopt a hash map if needed.
- `idn` is a convenience that pre-populates the universal `*IDN?`
  response unless a scene explicitly overrides it.

### 4. Match resolution at runtime

When `FakeBackend.QueryAsync` or `WriteAsync` is called and an active
scenario is loaded:

1. Look up the SCPI text in the scenario's scenes.
2. If found:
   - `respond` → return the response string (QueryAsync only; WriteAsync
     treats it as an error — see §6).
   - `ack` → return `Unit.Value` (WriteAsync only; QueryAsync treats it
     as an error).
   - `fail` → return the matching `BackendError` variant.
3. If not found, fall through to the existing programmatic DSL
   (`RespondToQuery` etc.), then to the universal defaults
   (`*IDN?` → configured IDN, otherwise echo).

This layering preserves backwards compatibility with the existing
FakeBackend tests; scenarios are an *additional* layer, not a
replacement.

### 5. CLI surface

The new top-level `mock` namespace owns the management commands:

```
ivicli mock scenario list
ivicli mock scenario create <name>
ivicli mock scenario remove <name>
ivicli mock scenario show <name>
ivicli mock scenario activate <name>
ivicli mock scenario deactivate

ivicli mock scenario <name> scene add  [--match <scpi>] [--respond <text> | --ack | --fail <variant>] [--fail-detail <value>]
ivicli mock scenario <name> scene list
ivicli mock scenario <name> scene remove <index>
```

The scene `<index>` is 1-based and stable across `list` invocations
within the same scenario revision.

### 6. Mismatch handling

When `WriteAsync` matches a `respond` scene, or `QueryAsync` matches an
`ack` scene, the FakeBackend returns a synthetic `BackendError`
(`MockScenarioContractMismatch`, a new variant of `BackendError`). This
makes scenario authoring errors visible at test time rather than
silently producing wrong data.

### 7. Application-layer ports

Following ADR 0010 §6 and §9.1:

- `IScenarioStore` (in `IviCli.Application.Mock`) exposes
  `LoadAsync(name)`, `SaveAsync(scenario)`, `ListAsync()`, `DeleteAsync(name)`.
- `TomlScenarioStore` (in `IviCli.Infrastructure.Mock`) is the
  file-backed adapter using `IFileSystem` and Tomlyn.
- `FakeScenarioStore` (in `IviCli.TestKit`) is the in-memory test double.

The active-scenario pointer reuses `ISessionStore` and lives in the
existing `SessionState` record as an optional `ActiveScenario` field;
this avoids introducing a third persistence file.

### 8. Domain types

`IviCli.Domain.Mock` houses:

- `MockScenario` (Name, IdnDefault, Scenes ImmutableArray)
- `MockScene` (Match, Action) where `Action` is a sealed sum type:
  `Respond(string Text)`, `Ack`, `Fail(string Variant, string? Detail)`
- `ScenarioName` Value Object (validation similar to `DeviceName`)

### 9. Error taxonomy

New per-use-case errors implement `IviError`:

- `ScenarioCreateError` (NameInvalid, AlreadyExists, StorageFailure)
- `ScenarioRemoveError` (NameInvalid, NotFound, StorageFailure,
  ActiveScenarioRefuses)
- `ScenarioActivateError` (NameInvalid, NotFound, SessionFailure)
- `ScenarioSceneAddError` (...similar shape)

The Backend-side `MockScenarioContractMismatch` is added to
`BackendError` so existing handlers map it via their
`*TransportFailure` variants without special-casing.

### 10. Compatibility with PRD/ADRs

- PRD §6 command namespace gains a new top-level `mock` entry. PRD §11.2
  command-naming rules (lower-case, verb-first) are honored.
- ADR 0009 §6 FakeBackend DSL remains intact; scenarios layer on top.
- ADR 0010 §6 per-assembly DI extensions add
  `AddIviCliMock()` exposing the new handlers and `IScenarioStore`.
- ADR 0014 §1 (per-domain sum errors), §9 (IviError contract) apply to
  every new error type.
- ADR 0017 §3 (log masking) — scenario IDN strings and response text are
  treated as user-supplied test fixtures and logged as-is at Debug. They
  do not pass through `ToLogString()` masking because by design they are
  not real instrument data.
- ADR 0021 layering — `IviCli.Domain.Mock` and
  `IviCli.Application.Mock` live in existing assemblies (no new
  csproj). The Infrastructure adapter lives in `IviCli.Infrastructure`.

### 11. Not in this ADR

The following are deliberately deferred:

- Pattern / regex matching on `match`.
- Ordered or state-machine scenes (e.g. "first call returns A, second
  returns B").
- Scenario import/export beyond manual file copy.
- Multi-device scenarios where different aliases get different
  behaviour.
- Recording from a real backend is handled by ADR 0027
  (`mock scenario record --from-script`); deterministic playback as a
  dedicated `IIviBackend` is split out into ADR 0028 (Replay Backend).

Each of these is a candidate for a follow-up ADR once v1 has real users.

## Consequences

**Pros**

- The CLI gains a coherent way for users to develop and demo against the
  Fake Backend without real instruments — directly servicing PRD §4
  Primary Users.
- The mocking knowledge stays in CLI-managed files; users compose
  scenarios in the same tool they use to drive instruments.
- Existing FakeBackend tests are unaffected (scenarios are an additive
  layer with deterministic fallthrough).

**Cons**

- Adds a new CLI namespace and several new files / commands to maintain.
- Table-lookup matching is intentionally limited; users who want
  stateful behavior will hit the limit and either wait for a follow-up
  ADR or use the programmatic DSL.
- Scenarios are written to plain TOML files; secrets must not be put in
  them (none are expected — these are test fixtures).

**Mitigations**

- Limitations are explicitly listed in §11 so users are not surprised.
- The CLI surface is small and mirrors the device-management commands
  (`add` / `remove` / `list` / `show`), so users familiar with `visa`
  already understand the shape.
- `MockScenarioContractMismatch` surfaces authoring mistakes loudly at
  the Backend boundary instead of silently corrupting data.
