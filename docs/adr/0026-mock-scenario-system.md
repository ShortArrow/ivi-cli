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

The top-level `mock` namespace owns the management commands, one group
per noun a mock is made of:

```
ivicli mock scenario list
ivicli mock scenario create <name>
ivicli mock scenario remove <name>
ivicli mock scenario show <name>
ivicli mock scenario activate <name> [--for <device>]
ivicli mock scenario deactivate

ivicli mock scene add <scenario> <scene>
ivicli mock scene remove <scenario> <scene>

ivicli mock rule add <scenario> --in <scene> --match <scpi> [--respond <text> | --ack | --fail <variant>] [--fail-detail <value>] [--transition-to <scene>] [--srq <status-byte>]
ivicli mock rule remove <scenario> <index>
```

`scene` and `rule` are siblings of `scenario` rather than children of it:
each verb names the scenario it operates on as an argument, so a
`scenario` path segment ahead of it would bind nothing while hiding two
of the three nouns from `ivicli mock --help`. The original
`mock scenario scene ...` / `mock scenario rule ...` paths keep working,
hidden from help, and are removed at 0.4.0.

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

`IviCli.Domain.Mock` houses (v0.2.0 shape, see §15 for the v0.1.x →
v0.2.0 evolution and the rename rationale):

- `MockScenario` (`Name`, `InitialScene`, `IdnDefault`,
  `Scenes ImmutableArray<MockScene>`) — a named behaviour package
  that may be a single-state config or a multi-state graph.
- `MockScene` (`Name SceneName`, `Rules ImmutableArray<MockRule>`)
  — a state node inside a scenario. The currently-active scene
  determines which rules are consulted; state-machine transitions
  (rule action `Transition(SceneName)`) move the active scene
  at runtime (issue #26 §"Implementation plan" — B0.2-3).
- `MockRule` (`Match`, `Action`) where `Action` is a sealed sum
  type: `Respond(string Text)`, `Ack`, `Fail(string Variant,
  string? Detail)`. This is what v0.1.x called `MockScene` —
  renamed in v0.2.0 to reclaim "scene" for the state-node role.
- `ScenarioName`, `SceneName` Value Objects (validation similar
  to `DeviceName`).

v0.1.x scenarios (flat rule list under `[[scenes]]`) load
transparently as a single synthetic `default` scene; see §15.

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

### 11. Not in this ADR (as of v0.2.0)

The following are deliberately deferred:

- Pattern / regex matching on `match`.
- Time-based / sequence rules (e.g. "the 3rd time you see X, fail").
- Scenario import/export beyond manual file copy.
- Multi-device scenarios where different aliases get different
  behaviour.
- **State-machine semantics** — `MockRule.Action` gains a
  `Transition(SceneName)` variant, and the FakeBackend tracks the
  current scene per active scenario, in B0.2-3 of issue #26
  ("Implementation plan"). The v0.2.0 shape (§8, §15) holds the
  type-system half of this contract; runtime behaviour ships
  alongside the CLI surface in B0.2-4.
- **Key-value variable state** (write `:VOLT 5.0` → later
  `:VOLT?` returns 5.0). Separate future issue; needed for
  continuous-variable mocks like voltage setpoint readback.
- Recording from a real backend is handled by ADR 0027
  (`mock scenario record --from-script`); deterministic playback
  as a dedicated `IIviBackend` is split out into ADR 0028
  (Replay Backend).

Each of these is a candidate for a follow-up ADR once v1 has real users.

### 12. Container packaging (Batch V follow-up)

[ADR 0018](0018-deployment-strategy.md) ships a mock-VISA container
(`ghcr.io/<owner>/ivi-cli-mock`) that bakes the FakeBackend +
scenario stack behind the gateway servers (HiSlip 4880, SOCKET
5025) for 3rd-party VISA-app e2e testing.

Runtime mock-control inside the container reuses every CLI verb
defined here via `docker exec`:

```
docker exec mock ivicli mock scenario create per-test
docker exec mock ivicli mock scene add per-test \
    --match "*MEAS?" --respond "1.234"
docker exec mock ivicli mock scenario activate per-test
```

This is the canonical entry point for unit-test-style "arm the
mock between assertions" flows. Adding HTTP endpoints for mock
CRUD over the Management API is a deferred follow-up if external
demand surfaces — see ADR 0018 §8 for the rationale.

The container also relies on a small composition-root env
`IVICLI_MOCK_ONLY=1` (introduced by Batch V) that collapses every
transport-specific backend to the FakeBackend fallback so the
gateway servers serve scenario responses without attempting any
outbound connection. The env is documented in ADR 0018 §10.

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

### 15. v0.2.0 evolution — Scenario / Scene / Rule hierarchy

v0.1.x conflated two roles into a single type called `MockScene`:

- 場面 (scene) = a state the mock is in,
- ルール (rule) = a single match → action pair.

The everyday meanings of "scenario" (脚本 / sequence over time) and
"scene" (場面 / one point in a sequence) made the v0.1.x model
mis-named once state-machine semantics were on the table — the
natural read of "the scenario switches scenes" should describe a
state transition inside one behaviour package, not a swap of the
top-level behaviour package itself.

v0.2.0 reclaims the language:

| Concept | v0.1.x | v0.2.0 |
| --- | --- | --- |
| 全体 (behaviour package, possibly a state graph) | `MockScenario` | `MockScenario` |
| 場面 (state node) | _(folded into rule)_ | `MockScene` |
| 1 行のセリフ (match → action) | `MockScene` | `MockRule` |
| Sum-type of actions | `SceneAction` | `RuleAction` |

`MockScenario` gains an `InitialScene` field (which scene is
active on `mock scenario activate`). State-machine transitions
land in B0.2-3 as a new `RuleAction.Transition(SceneName)`
variant. Until then, a scenario is effectively a single-scene
graph and the behaviour observable to existing users is
unchanged.

Backwards compatibility:

- v0.1.x TOML scenarios continue to load. The parser
  (`TomlScenarioParser.Parse`) wraps every flat `[[scenes]]`
  table as a rule in a synthetic scene named `default`, and
  designates it as the `InitialScene`. `Serialize` round-trips
  back to the v0.1.x flat shape until v0.2.0's multi-scene
  schema (`[[scenes]] name = "..."` + nested
  `[[scenes.rules]]`) ships in B0.2-2.
- The `IScenarioStore.AppendSceneAsync(MockScene)` port from
  v0.1.x is renamed to `AppendRuleAsync(MockRule)` and appends
  to the scenario's initial scene.
- `mock scenario show` still flattens to a single ordered list
  for v0.1.x parity until B0.2-4 lands the multi-scene view.
- The scene-adding CLI verb continues to function with the same
  arguments (it appends a rule to the initial scene, same observable
  behaviour as v0.1.x). It was spelled `mock scenario scene add ...`
  at the time; §5 records where it lives now.

Tracked in [issue #26](https://github.com/ShortArrow/ivi-cli/issues/26).

### 16. v0.2.4 evolution — per-device scenario bindings

v0.1.x — v0.2.3 stored a single global active scenario in
`SessionState.ActiveScenario`. With v0.2.3's HiSLIP sub-address
multiplexing one gateway can route to several backend devices,
but every device that landed on the FakeBackend got the *same*
scenario forced onto it — multi-device mocks were only useful
when every device wanted the same state machine simultaneously.

v0.2.4 replaces the single field with an explicit per-device
map:

```csharp
public sealed record SessionState(
    DeviceName? CurrentDevice,
    ImmutableDictionary<DeviceName, ScenarioName> DeviceScenarios
);
```

The FakeBackend gains a per-device binding table
(`ConcurrentDictionary<DeviceName, ActiveBinding>` where
`ActiveBinding` holds the scenario plus that device's current
scene), and `IScenarioAwareBackend` gains a per-device probe
`HasActiveScenarioFor(Device)`. `DefaultBackendFactory` consults
the per-device probe instead of the global flag, so a device
without a binding still dispatches to its real transport
backend on the same gateway. State-machine transitions remain
per-device — `OUTP ON` on `psu1` does not move `psu2`'s scene.

**CLI shape.** `ivicli mock scenario activate <name>` keeps the
single-arg form, but the binding target is the *current device*
(set by `ivicli visa use <device>`). Explicit binding is
`--for <device>`:

```
ivicli visa use psu1
ivicli mock scenario activate psu-fsm                   # binds to psu1
ivicli mock scenario activate dmm-noise --for dmm0      # bind ad-hoc
ivicli mock scenario list-active                        # show bindings
ivicli mock scenario deactivate --for dmm0              # clear one
```

Calls fail with `ActivateScenarioNoDeviceSelected` when
`--for` is omitted and no current device is set, so the user
gets a precise CLI message rather than silently binding to
nothing.

**Persistence migration.** `state.json` adds a new
`device_scenarios` map; the legacy `active_scenario` field is
read-only and promotes to the binding for the then-current
device on first load. Old files written by v0.1.x — v0.2.3
upgrade transparently. When the legacy state had no current
device the binding has nowhere to go and is dropped silently —
the user re-activates explicitly under the new shape.

**Environment variable.** `IVICLI_SCENARIO` binds the named
scenario to the current device on startup, mirroring the CLI
default. Without a current device the variable logs a warning
and is ignored.

Tracked in [issue #36](https://github.com/ShortArrow/ivi-cli/issues/36).

### 17. Live re-binding — a serving gateway observes an out-of-process `activate`

A gateway populates the FakeBackend's per-device bindings once, at
process startup, from the session store (`ActivateScenarioIfRequested`).
Those bindings then live only in that process's memory. A subsequent
`ivicli mock scenario activate <name>` is a *separate* CLI process: it
writes the new binding into the shared session store but cannot touch the
serving gateway's memory. A client that holds a long-lived connection
therefore kept seeing the scenario that was active when the gateway
started — the swap only took effect after a gateway restart. This is the
common operator loop (drive a mock over one connection, swap its behaviour
between test steps), so the gap was worth closing.

**Decision.** An Application-layer port `IScenarioBindingRefresher`
re-syncs a device's binding from the persisted session into the running
scenario-aware backend. Each LAN gateway — SOCKET, HiSLIP, and VXI-11 —
invokes it before dispatching an incoming SCPI operation (per request line
for SOCKET, per message for HiSLIP, per completed write for VXI-11), so a
new binding is picked up on the next operation without reconnecting.

Re-application is **scoped to a changed scenario name**: an unchanged
binding is left untouched so in-flight scene / transition state (e.g. a
power-supply state machine the client already toggled to `on`) survives
the frequent reconnects real apps perform. A session that no longer binds
the device deactivates the running binding. The port is **no-throw**;
gateways still guard the call and log, so a store hiccup never drops a
live connection.

**Wiring.** `SessionScenarioBindingRefresher` (the session/scenario-store
implementation) lives in the Fake backend assembly and is registered by
`AddIviCliBackendsFake`. Gateways take the refresher as an *optional*
constructor dependency defaulting to a no-op `NullScenarioBindingRefresher`;
a gateway composed without the Fake backend simply retains its
startup bindings — the pre-feature behaviour — rather than failing to
resolve. Applying the refresh uniformly across all three gateways keeps
the mock container's HiSLIP (`4880`) and SOCKET (`5025`) endpoints
behaving identically for the same `activate`.

**Cost.** The refresh re-reads the session store on every operation. For a
mock's request rate this is negligible, and it buys a restart-free operator
loop; a higher-throughput path would cache and invalidate instead.
