# Changelog

All notable changes to ivi-cli are documented here. Format roughly follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning follows
[Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html).

## [0.2.5] — 2026-06-05

### Fixed

- **Mock-VISA container: scenario auto-load** (regression in
  v0.2.4). The v0.2.4 per-device scenario rewrite started ignoring
  `IVICLI_SCENARIO` when no current device was selected, which
  broke the pre-baked container — its `session.json` is empty by
  design and `IVICLI_SCENARIO=default` was previously enough to
  arm the gateway. The Dockerfile now ships
  `IVICLI_SCENARIO_FOR=mock1` so the env-driven activation has an
  explicit target. The release-time smoke test exercises this
  path; v0.2.4's container build failed it (no GitHub Release
  was cut for v0.2.4).

### Added

- **`IVICLI_SCENARIO_FOR=<device>` env var** — pairs with
  `IVICLI_SCENARIO` to bind the named scenario to a specific
  device at startup, without needing `state.json` written first.
  Resolution order is now `IVICLI_SCENARIO_FOR` → session's
  current device → warning + skip. Invalid device names log a
  warning and fall through to the session default.

### Compatibility

- Consumers that relied on v0.2.4's nupkg (NuGet `ivicli 0.2.4`)
  can continue using it locally — the regression only affected
  the container image. Container users should pull
  `ghcr.io/shortarrow/ivi-cli-mock:0.2.5` once the release
  workflow finishes.

## [0.2.4] — 2026-06-05

### Added

- **Per-device scenario bindings** (issue #36). The single global
  active-scenario field is replaced by an explicit per-device
  binding: `ivicli mock scenario activate <name> --for <device>`
  binds the scenario to one device on a gateway, while other
  devices on the same `FakeBackend` may carry distinct scenarios
  active simultaneously. The `--for` flag defaults to the current
  device set by `ivicli visa use <device>`, so the common case
  stays one command. Explicit `--for` still works for ad-hoc
  binding without changing the session pointer.
- **`ivicli mock scenario list-active`** — lists every device that
  currently has a scenario bound, optionally as `--json`. The
  current device is marked with a trailing `*`.
- **`ivicli mock scenario deactivate --for <device>`** —
  symmetric counterpart that clears a single binding (the same
  current-device default applies).

### Changed

- **`SessionState` shape** — `active_scenario` (single global
  field) → `device_scenarios` (map of device → scenario).
  Existing `state.json` files written by v0.1.x — v0.2.3 are
  migrated on first read: an old `active_scenario` is promoted
  to the binding for the then-`current_device` and the legacy
  field is dropped on save. When the legacy state had no current
  device, the binding has nowhere to attach and is dropped — the
  user re-activates explicitly. No further user action required.
- **`IScenarioAwareBackend`** gained `HasActiveScenarioFor(Device)`.
  `DefaultBackendFactory` now short-circuits to the FakeBackend
  only for devices that *actually* have a scenario bound; devices
  without a binding continue to dispatch to their real transport
  backend.
- **`IVICLI_SCENARIO` env var** binds the named scenario to the
  current device on startup, matching the `--for` default
  fallback. Without a current device the variable is ignored
  with a logged warning (there's no device to bind to).

### Why

v0.2.3 unlocked HiSLIP sub-address multiplexing (one gateway,
multiple backend devices), but a single global scenario meant
that activating a state machine for one device unconditionally
gave the same state machine to every other device on the
gateway. Multi-device mocks were only useful as long as every
device wanted the same scenario at once — which is rarely the
case. v0.2.4 lifts that limitation while keeping the single-
device workflow unchanged.

## [0.2.3] — 2026-06-04

### Fixed

- **HiSLIP gateway: sub-address multiplexing** (issue #21). A
  single gateway server can now route incoming HiSLIP sessions to
  distinct backend devices based on the client-supplied sub-address
  in the Initialize payload (IVI-6.1 §10.2.1). Two routes with
  endpoints `hislip0` and `hislip1` on the same `[[servers]]` entry
  serve the corresponding devices on the same TCP port — one
  container or one gateway process can now expose a virtual lab of
  several mock instruments without needing one TCP port per
  device. v0.1.x — v0.2.2 silently ignored the sub-address and
  picked the scenario's first route on the server, which
  prevented this and made the user-facing behaviour incoherent
  with the wire-level protocol.

  When no route matches the supplied sub-address, the gateway now
  returns a HiSLIP Fatal at handshake time (logged at INF) instead
  of silently serving the wrong device. Operators with broken
  route configs (endpoint name mismatching the wire sub-address)
  will see the failure surface immediately.

### Migration

- `server route add <server> <endpoint> <device>` must use an
  endpoint string that matches the LAN-device segment of the VISA
  resource clients dial in with — `hislip0` / `hislip1` / etc. for
  HiSLIP, the TCP port number for SOCKET. The PSU sample
  (`docs/samples/psu/`) and the prior release-day idg setup
  already use `hislip0`, so no change for existing deployments.

## [0.2.2] — 2026-06-03

### Added

- **`mock scenario create --initial <scene>`** — choose the
  starting scene at create time. The scenario opens with that
  single (empty) scene as both its only scene and its
  `InitialScene`. Without the flag, the v0.1.x-compatible
  synthetic `default` scene shape is preserved. Eliminates the
  v0.2.0 footgun where a freshly-created scenario was stuck in
  an empty `default` scene unless the user hand-edited TOML or
  used `scene add` workarounds.

  Example end-to-end FSM setup that previously required scp'ing
  a TOML now stays entirely in the CLI:

  ```sh
  ivicli mock scenario create psu-bench --initial off
  ivicli mock scenario scene add psu-bench on
  ivicli mock scenario rule add psu-bench --in off \
      --match 'OUTP ON' --ack --transition-to on
  ivicli mock scenario rule add psu-bench --in on \
      --match 'OUTP OFF' --ack --transition-to off
  ```

## [0.2.1] — 2026-06-03

### Fixed

- **HiSlip clients now reach the gateway even when a scenario is
  active.** v0.1.3 introduced a scenario-aware short-circuit in
  `DefaultBackendFactory` that collapsed **every** dispatch to the
  FakeBackend when a mock scenario was active. The intent was to
  let the gateway answer from the mock for placeholder INSTR
  resources without timing out trying to TCP-connect to a real
  VXI-11 / SOCKET endpoint. Side-effect: client invocations
  (`ivicli visa query/write`) targeting a HiSlip endpoint
  (`...::hislip0::INSTR`) were ALSO re-routed to the client process's
  local FakeBackend, so they never crossed the wire to the gateway.
  Every new ivicli CLI call re-activated the scenario and reset the
  FSM to the initial scene, so `OUTP ON` followed by `OUTP?` always
  saw the `off` state's response.

  The fix narrows the short-circuit: HiSlip resources are now
  always dispatched to `HiSlipBackend`, regardless of scenario
  activation. The user typed `...::hislip0::INSTR` to explicitly
  reach a network HiSlip endpoint (typically the ivi-cli gateway
  itself); honouring that intent preserves FSM behaviour across
  CLI calls. VXI-11 / SOCKET / placeholder TCPIP-INSTR / USB /
  GPIB short-circuits still apply.

## [0.2.0] — 2026-06-03

This release reshapes the mock-scenario domain so that **scenes are
state nodes and scenarios are state machines**, removing a semantic
mismatch that v0.1.x lived with (see issue
[#26](https://github.com/ShortArrow/ivi-cli/issues/26) and ADR 0026
§15 for the design rationale).

The shape is a deliberate breaking change at the .NET library API
and at the CLI verb surface. Existing **scenario TOML files** are
backwards-compatible — they continue to load as a single
synthetic `default` scene.

### Added

- **State-machine scenarios** — every `RuleAction` carries an
  optional `Transition: SceneName?`. When set, the FakeBackend
  swaps the active scenario's current scene immediately after the
  rule's effect is applied, so the same SCPI query can produce
  different responses across the session
  (e.g. `OUTP?` returns `0` until `OUTP ON` walks the FSM to a
  scene where `OUTP?` returns `1`).
- **New CLI verbs** under `mock scenario`:
  - `scene add <scenario> <scene>` — create an empty state node.
  - `scene remove <scenario> <scene>` — remove a state node by
    alias (refuses to remove the initial scene).
  - `rule add <scenario> --in <scene> --match X --respond Y
     [--transition-to <scene>]` — append a rule to a named
    scene with an optional transition.
  - `rule remove <scenario> <index> [--in <scene>]` — remove a
    rule by 1-based index inside the target scene.
  - `mock scenario show` now prints the multi-scene tree with the
    initial scene marked `*` and per-rule transitions inline.
- **v0.2.0 TOML schema** — `initial_scene` + `[[scenes]]` tables
  with `name` + nested `[[scenes.rules]]`. Existing v0.1.x flat
  scenarios (no `name`, flat `[[scenes]]` with `match`) keep
  loading as a single synthetic `default` scene. Serialisation
  emits the flat shape for single-default-scene scenarios with no
  transitions so pre-v0.2 files round-trip unchanged.
- **PSU sample upgraded to a 2-state FSM** — `docs/samples/psu/`
  now demonstrates `off → on → off` walking via `OUTP ON` /
  `OUTP OFF`, with `OUTP?` and `MEAS:VOLT?` flipping per state.
  `psu-bench.toml`, `setup.sh`, and `setup.ps1` all switched to
  the new shape.

### Changed (breaking)

- `IviCli.Domain.Mock.MockScene` is **renamed**: the v0.1.x type
  (a single `match` → `action` pair) is now
  `IviCli.Domain.Mock.MockRule`; the name `MockScene` is
  re-purposed as the state-node type (`Name SceneName`, `Rules
  ImmutableArray<MockRule>`).
- `IviCli.Domain.Mock.SceneAction` → `IviCli.Domain.Mock.RuleAction`
  with the same variants (`Respond` / `Ack` / `Fail`).
- `IviCli.Domain.Mock.MockScenario`'s shape: now
  `(Name, InitialScene SceneName, IdnDefault, Scenes
  ImmutableArray<MockScene>)`. The v0.1.x convenience factory
  `MockScenario.SingleScene(name, idnDefault, rules)` covers the
  legacy flat-rule path used by traffic-record imports.
- `IScenarioStore.AppendSceneAsync(MockScene)` → `AppendRuleAsync(MockRule)`,
  with the documented contract of appending to the scenario's
  initial scene.
- CLI verbs `mock scenario scene add --match …` and
  `scene remove <index>` are gone — use the new `scene` /
  `rule` verbs above. Scripts that drove the v0.1.x verbs need a
  one-time rewrite.
- Audit log `Operation` codes: `scene.add` / `scene.remove` now
  describe state-node operations (target =
  `<scenario>/<scene>`); rule-level mutations use the new
  `rule.add` / `rule.remove` codes (target =
  `<scenario>/<scene>/<match-or-index>`). Tools that grep the
  NDJSON audit log need to widen their recognised set.

### Internals

- New optional capability interface
  `IviCli.Application.Backends.IScenarioAwareBackend`
  (introduced in v0.1.3); the FakeBackend now also tracks its
  current scene per active scenario and applies transitions
  under a single internal lock.

### Migration

- **Authoring**: drop in the new v0.2.0 TOML manually, or copy the
  PSU sample (`docs/samples/psu/psu-bench.toml`) as a starting
  point.
- **Existing v0.1.x scenarios**: no action required — load as a
  single `default` scene; subsequent `rule add` / `rule remove`
  without `--in` continues to operate on that scene.
- **CLI**: replace `mock scenario scene add … --match …` with
  `mock scenario rule add … --match …` (optionally
  `--in <scene>` and `--transition-to <scene>`).
- **Library consumers**: rename `MockScene` → `MockRule`,
  `SceneAction` → `RuleAction`. Use
  `MockScenario.SingleScene(...)` for v0.1.x-equivalent
  construction.

## [0.1.4] — 2026-06-03

### Fixed

- **Mock scenario scene match now normalises the SCPI leading-colon
  prefix** (real-environment interop). At message start, SCPI 1999
  §6.1.1 / IEEE 488.2 §7.5 treat `:OUTP` and `OUTP` as equivalent —
  the colon is the "absolute path from root" prefix and there is no
  current path to be relative to. Real VISA clients (NI-VISA,
  Keysight, PyVISA, ImageDataGetter via NI-VISA) freely emit the
  colon-prefixed form, so a scene registered as `MEAS:VOLT?` now
  also matches `:MEAS:VOLT?` (and vice versa). Eliminates the need
  to register redundant `:`/non-`:` scene pairs.

## [0.1.3] — 2026-06-03

### Fixed

- **Active mock scenario now outranks resource-shape dispatch**
  (issue #25). When `ivicli mock scenario activate <name>` has
  been called, `DefaultBackendFactory.CreateFor` short-circuits
  to the FakeBackend regardless of the device's `VisaResource`
  shape. Previously a placeholder TCPIP-INSTR resource still
  routed traffic to the VXI-11 / HiSLIP / SOCKET / Local
  backends, so the gateway tried (and timed out at ~2.5 s) to
  open a real transport connection against a port nothing was
  listening on. Net effect for sample users: the `IVICLI_MOCK_ONLY=1`
  workaround is no longer required for the gateway side —
  `setup.sh` and `setup.ps1` drop it. The env var still works
  as a manual override (the container path keeps using it).
- Introduces a new optional capability mixin
  `IScenarioAwareBackend` (in `IviCli.Application/Backends/`)
  that the FakeBackend implements; the factory consults this
  on its fallback backend at dispatch time.

## [0.1.2] — 2026-06-03

### Fixed

- **HiSLIP gateway SCPI termination + MessageId echo** (critical
  interop). Two spec violations surfaced when a real NI-VISA
  client (NI MAX) wrote `*IDN?\n` to the mock gateway and the
  read returned `VI_ERROR_CONN_LOST` (0xBFFF00A6):
  - `HiSlipGatewayServer.DispatchScpiAsync` passed the raw,
    terminator-included SCPI string to `ScpiQuery.From` and to
    the backend. Scenario scenes registered with
    `--match '*IDN?'` (no terminator) did not match
    `*IDN?\n`, so `FakeBackend` echoed the query instead of
    answering with the scene response. Fix: trim
    trailing `\r\n` at the gateway boundary before invoking
    the backend, per IEEE 488.2 §7.5.
  - `HiSlipGatewayServer.SendDataEndAsync` hardcoded
    `messageParameter: 0` on every server-to-client response,
    violating IVI-6.1 §10.6.2 which requires the server to
    echo the MessageId of the client's initiating Data /
    DataEnd. Real clients close the TCP connection when the
    parameter doesn't match. Fix: thread the client's
    MessageId through `DispatchScpiAsync` into
    `SendDataEndAsync`.

### Known

- `IVICLI_MOCK_ONLY=1` is still required on the gateway
  process to opt into FakeBackend dispatch when the device
  resource is a placeholder TCPIP INSTR — tracked as #25.
  `docs/samples/psu/{setup.sh,setup.ps1}` set this env var
  automatically as of 0.1.1.

## [0.1.1] — 2026-06-03

### Fixed

- **HiSLIP wire-format bug** (critical interop) — the message
  header treated the IVI-6.1 §10.1.1 prologue as a single byte
  (`'S'` 0x53) instead of the spec's 2-byte ASCII `"HS"`
  (0x48 0x53). This shifted every subsequent field by one
  octet, so the gateway was effectively speaking a non-HiSLIP
  protocol on the wire. ivi-cli ↔ ivi-cli sessions happened to
  work because both ends shared the same offset error, but any
  spec-compliant client (NI-VISA / Keysight / R&S / PyVISA-py)
  immediately closed the connection with a prologue mismatch
  (surfaced to NI-VISA users as `0xBFFF00A6`). A
  byte-sequence test against an IVI-6.1 example now guards the
  layout.

### Docs

- New `docs/samples/psu/` walks the minimum CLI to stand up a
  PSU mock VISA device over HiSLIP / SOCKET. Ships
  `psu-bench.toml` (drop-in scenario), `setup.sh` (bash
  idempotent walker for Linux / macOS / WSL / Git Bash), and
  `setup.ps1` (PowerShell-native equivalent that also prints
  NI MAX manual-registration steps for apps that go through
  NI-VISA / Keysight VISA, e.g. ImageDataGetter).
- ADR 0020 §12 marks NuGet auth keyless via Trusted Publishing
  (OIDC); the `NUGET_API_KEY` secret row was removed.

### Build / CI

- `release.yml` and `nightly.yml` declare workflow-level
  `defaults.run.shell: bash` so Windows runners stop parsing
  bash `\` line continuations as PowerShell unary operators.
- `release.yml` `pack` job switches to NuGet Trusted Publishing
  (OIDC) via `NuGet/login@v1` — no long-lived `NUGET_API_KEY`
  secret required.
- `release.yml` `docker` job lowercases the ghcr.io owner before
  composing image tags (OCI registries reject uppercase).
- `release.yml` `github release` job bundles per-RID
  self-contained and framework-dependent publishes into
  `ivicli-X.Y.Z-<rid>-{selfcontained,fxdep}.zip` archives
  before attaching, so the Release page shows one download
  per platform/flavor instead of a flat dump of loose .dll /
  .pdb files.
- `IviCli.Cli.csproj` declares `IsPackable`, `PackAsTool`,
  `PackageId=ivi-cli`, `ToolCommandName=ivicli`, plus
  description / tags / repository metadata so `dotnet pack`
  actually produces a nupkg (the parent
  `src/Directory.Build.props` sets `IsPackable=false` for
  library projects).

### Known issues filed

- #14 VXI-11 in mock-VISA container
- #15 NativeAOT mock-VISA image
- #16 Management API TLS certificate hot-reload
- #17 OTel Activity emission from gateway servers
- #18 Local backend SRQ / AssertTrigger parity
- #19 Tighten Windows ACL on session.json
- #20 VXI-11 client backend: real portmapper round-trip
- #21 HiSlip gateway: sub-address multiplexing for multi-device
- #22 CLI tree: surface `mock scene` as a peer of `mock scenario`
- #23 DeviceName: actionable error message for hyphen / uppercase / dot
- #24 PowerShell-native sample scripts (partially resolved here)

## [0.1.0] — 2026-05-29

Initial public release. Covers Phase 1 (CLI core), Phase 2 (gateway servers
+ backend pooling + plugins), and Phase 3 (Management API + WebSocket + PAT
+ TLS + OpenTelemetry + audit log + scenario-driven mock VISA container).

### Added — Phase 1: CLI core

- Stateful `visa` namespace: `add` / `remove` / `list` / `use` / `current` /
  `scan` / `query` / `write` / `read` / `status` / `script` / `monitor` /
  `watch` / `lint`. Aliases persist across invocations
  (`config.toml` at the platform-specific XDG-style path).
- Multiple backends behind a single `IIviBackend` port: Local NI-VISA,
  HiSLIP, VXI-11, raw TCP SOCKET, Fake (programmable + scenario playback),
  Replay (strict deterministic replay).
- `visa lint` for SCPI scripts — IEEE 488.2 + SCPI core vocabulary
  enforcement without touching hardware.
- `IVICLI_CAPTURE=<path>` streams every backend operation to an
  append-only NDJSON log for support / post-hoc inspection.
- Cross-platform binaries for win-x64/arm64, linux-x64/arm64,
  osx-x64/arm64 plus a `dotnet tool` nupkg.

### Added — Phase 2: Gateway + ecosystem

- Gateway servers: HiSlip (4880), VXI-11 (1024), raw SOCKET (5025).
  Expose a local instrument over the wire so PyVISA / NI-VISA clients can
  drive it without a redeploy.
- Backend session pooling (ADR 0038) — `[pool]` config table.
- Plugin / extension loader (ADR 0013) — vendor backends as
  AssemblyLoadContext-isolated NuGet packages.
- HiSlip v3 protocol (`AsyncMaximumMessageSize`, locking sub-protocol),
  VXI-11 abort + Interrupt channel, raw-socket SCPI framing.
- `mock scenario record --from-script` captures real SCPI traffic for
  later deterministic replay via `IVICLI_REPLAY=<scenario>`.

### Added — Phase 3: Management API + security + observability

- HTTP JSON API at `http://127.0.0.1:8080/v1` (ADR 0034) with
  `/openapi/v1.json`. List devices, query/write SCPI, read status —
  designed for AI agent / dashboard / CI integration.
- WebSocket at `/v1/devices/{name}/visa` (ADR 0035) for streamed SCPI.
- PAT authentication (ADR 0036) — `Authorization: Bearer` for HTTP,
  `ivi-cli-pat.<token>` sub-protocol for WebSocket; hash-only at-rest.
- PAT scopes + token expiry (ADR 0044) — `--scope read:devices /
  read:servers / read:scenarios / write:scpi`; `--expires 7d | 12h | 5m
  | ISO-8601`.
- TLS / mTLS for the Management API (ADR 0039) — opt-in via
  `[api.tls] enabled = true`; PFX / PEM cert paths + auto-generated
  loopback self-signed for dev.
- OpenTelemetry exporter (ADR 0040) — Activity + Meter; OTLP endpoint
  via `[telemetry]`.
- NDJSON audit log (ADR 0043) — append-only host-filesystem timeline
  of every auth attempt, API request, config mutation, and gateway
  lifecycle transition.

### Added — IVI ecosystem introspection (Batch Y)

- `ivicli driver list` — enumerates `<SoftwareModule>` entries in
  the local `IviConfigurationStore.xml` (name, description, module
  path, prefix). Pure-managed XML parsing; no vendor SDK / no COM
  interop. Non-Windows hosts get a friendly
  `(no IVI Configuration Store at …)` instead of an error.
- `ivicli logical list` — enumerates `<LogicalName>` entries
  (name, description, bound driver-session). Same parser, same
  graceful-degradation rules. See ADR 0045 for the full
  integration design.

### Added — Phase 3 follow-ups

- **Mock-VISA container** (ADR 0018) — `ghcr.io/<owner>/ivi-cli-mock`
  multi-arch (amd64 + arm64) image bundles the gateway server +
  scenario-backed FakeBackend. `docker run -p 4880:4880 -p 5025:5025
  ghcr.io/...` gives 3rd-party VISA-app developers a scriptable mock
  instrument with zero hardware. `docker exec ivicli mock scene add …`
  is the runtime control plane.
- **`visa scan` discovery** (ADR 0008) — LXI mDNS / DNS-SD via
  Makaretu.Dns + VXI-11 portmapper UDP broadcast. `visa scan --add`
  auto-registers every responder with a deterministic alias.
- **SOCKET resource shape** — `TCPIP::host::port::SOCKET` now parses
  to its own `VisaResource.TcpipSocket` variant and dispatches to
  `SocketBackend` (Batch X).
- **HiSlip healthcheck friendliness** — Docker HEALTHCHECK TCP probes
  no longer generate scary `LogError` stack traces in `docker logs`;
  early-handshake disconnects land at Debug (Batch X).
- **Audit subject + ConfigMutated wiring** (ADR 0043 follow-up) — 11
  config-mutating handlers emit `ConfigMutated` with
  `subject = cli/{Environment.UserName}` on success; gateway
  start/stop/crashed transitions emit `ServerLifecycle`.

### Documentation

- Bilingual lockstep (English primary + Japanese mirror) enforced by
  pr.yml `docs-sync-check`. PRD, README, CONTRIBUTING.
- Architecture Decision Records 0001–0044 cover the design choices.
- Quick start with Docker + Quick start with CLI in README.

### Testing

- 592 unit + architecture tests at the time of v0.1.0 cutting.
- Husky pre-commit (CSharpier) + pre-push
  (`dotnet test --filter Category!=Integration`).
- `pr-docker-smoke.yml` paths-filtered Docker smoke for PRs touching
  the container or CLI entrypoint.
- `release.yml` Docker job runs a pre-push smoke against the freshly
  built image before pushing the multi-arch manifest.

### Known limitations

- VXI-11 in container deferred (RPC dynamic ports break Docker port
  mapping; ADR 0018 §Out-of-scope).
- NativeAOT image deferred — dependency stack (Serilog, OpenTelemetry,
  ASP.NET Core, Tomlyn, plugin reflection) needs AOT-compat audit.
- Cert hot-reload deferred (ADR 0039 §Out-of-scope) — long-running API
  servers must restart to pick up rotated certs.
- Gateway-server OpenTelemetry Activity emission deferred (ADR 0040
  §Out-of-scope) — Activity source reserved but unused.
- Local backend SRQ / AssertTrigger deferred (ADR 0041) — HiSlip /
  VXI-11 backends are at parity; Local needs IVI.NET reflection
  follow-up.

[0.2.3]: https://github.com/ShortArrow/ivi-cli/releases/tag/v0.2.3
[0.2.2]: https://github.com/ShortArrow/ivi-cli/releases/tag/v0.2.2
[0.2.1]: https://github.com/ShortArrow/ivi-cli/releases/tag/v0.2.1
[0.2.0]: https://github.com/ShortArrow/ivi-cli/releases/tag/v0.2.0
[0.1.4]: https://github.com/ShortArrow/ivi-cli/releases/tag/v0.1.4
[0.1.3]: https://github.com/ShortArrow/ivi-cli/releases/tag/v0.1.3
[0.1.2]: https://github.com/ShortArrow/ivi-cli/releases/tag/v0.1.2
[0.1.1]: https://github.com/ShortArrow/ivi-cli/releases/tag/v0.1.1
[0.1.0]: https://github.com/ShortArrow/ivi-cli/releases/tag/v0.1.0
