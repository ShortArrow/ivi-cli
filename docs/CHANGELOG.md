# Changelog

All notable changes to ivi-cli are documented here. Format roughly follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning follows
[Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **A NativeAOT flavor of the mock-VISA container** (#15).
  `docker build -f docker/Dockerfile.aot .` produces the same mock
  surface (HiSLIP + SOCKET + VXI-11, same config, same smoke) at
  58 MB compressed instead of 143 MB, with SCPI-facing startup 17×
  faster; the publish flavor behind it is
  `dotnet publish -p:MockContainerAot=true`. CI builds and smokes it
  on every container-touching PR. The image is not yet published to
  ghcr.io — that needs an arm64 build and a decision on invariant
  globalization — and the issue's <30 MB target is documented as
  unreachable on the debian base (47 MB of it is base layers).

### Fixed

- **Release assemblies are built reproducibly** and NuGet Package
  Explorer's health check stops reporting "Non deterministic".
  `ContinuousIntegrationBuild` now turns on for GitHub Actions builds, so
  the PDBs map every source path to `/_/` through SourceLink instead of
  recording the runner's absolute paths; local builds are unchanged so
  debugger stepping keeps working.

### Changed

- **JSON serialization is source-generated** (#15, first step toward the
  NativeAOT image). The audit NDJSON, session file, capture NDJSON,
  WebSocket frames, and API error/health bodies now serialize through
  `JsonSerializerContext` types instead of reflection; the bytes on disk
  and on the wire are unchanged, and the existing shape-pinning tests
  prove it. TOML follows the same pattern: every Tomlyn surface maps
  `TomlTable` by hand, so one generated `TomlModelContext` covers config,
  scenarios, API tokens, and plugin manifests. Trim diagnostics drop from
  25 to 4 (only the plugin loader remains). The loader itself is now
  behind the `IviCli.Plugins.IsSupported` feature switch — a trimmed/AOT
  publish pins it to false and drops the reflection loader, a JIT build
  is unchanged — and the minimal-API endpoints compile through the
  request-delegate generator with every body type in the JSON context.
  Trim and AOT analyzers now run on every build, so a new reflection
  call fails CI instead of resurfacing at the next NativeAOT publish.

## [0.3.0] — 2026-08-21

### Added

- **Third-party attribution ships with the binaries** (#152). A
  `THIRD-PARTY-NOTICES.md` lists every package in the CLI's dependency
  closure with the copyright line its license asks a redistributor to carry,
  and travels — with `LICENSE-MIT` and `LICENSE-APACHE` — inside the per-RID
  archives, the container image, and the tool package. `Ivi.Visa.dll` is the
  reason it matters most: the IVI Foundation grants redistribution "provided
  that the above copyright notice(s) appear in all copies". Six of the
  packages declare no usable license metadata, so the entries were read from
  the projects themselves; a pull-request check keeps the list complete
  against `src/IviCli.Cli/packages.lock.json`.

- **Local NI-VISA backend SRQ and trigger delivery is now observed, not
  assumed** (#18). A pair of bench-gated integration tests points
  `LocalBackend.ServiceRequestStream` and `TriggerAsync` at the virtual USB
  mock attached through usbip-win2: the IEEE 488.2 sequence delivers the
  rule's status byte, and `*TRG` raises a distinct one. The tests skip
  wherever the mock is not attached, so CI is unaffected; the scenario and
  attach steps ship next to the tests.

- **Gateway servers emit OpenTelemetry spans** (#17). Each HiSLIP /
  VXI-11 / SOCKET connection carries a `gateway.session` span and every
  handled operation a `gateway.message` child, with the backend's spans
  nested inside — so a trace shows gateway → backend → device. A HiSLIP
  client can additionally join the caller's trace across processes: with
  `[telemetry] hislip_propagation = true` it precedes each operation with a
  vendor-specific message (type 128) carrying the W3C trace context, which
  the gateway consumes. Off by default — IVI-6.1 lets a conforming foreign
  server answer an unrecognized vendor message with a non-fatal error, so
  the flag is for peers known to be ivi-cli gateways.

- **The Management API picks up rotated TLS certificates without a
  restart** (#16). The listener re-reads the files under `[api.tls]` when
  their timestamps move (5 s poll) and serves the new certificate from the
  next handshake; a rotation that fails to load or is already expired is
  rejected with a warning while the old certificate stays active — a failed
  load is retried on the next poll until it heals, so a half-written pair
  or a briefly locked file cannot lose the rotation — and each successful
  reload lands in the audit log as `server.lifecycle` / `cert-reloaded`. Operators rotating via ACME or corporate PKI cron jobs
  no longer schedule daemon restarts.

- **The mock-VISA container serves VXI-11, and the gateway answers UDP
  portmapper probes** (#14). The VXI-11 gateway already multiplexed
  portmap and Device Core on one TCP port; what was missing was a UDP
  responder on 111 — the transport `visa scan`'s broadcast probe and
  unicast portmap clients actually use — and a container that exposes it.
  `docker run -p 111:111 -p 111:111/udp` now serves VXI-11 end-to-end
  (the CI smoke drives ivicli's own VXI-11 client through it); broadcast
  discovery of a bridge-networked container is not possible (broadcasts
  are not DNATed) and still needs `--network host`.

- **A mock instrument can be a USB device** (#118, #119, #121, #122, #124,
  #126, #128, #138). A new gateway type, `server add --type usbip`, exports
  each routed device over the USB/IP protocol; a USB/IP client on the host
  (usbip-win2 on Windows, the kernel's `vhci-hcd` on Linux) attaches it as
  if it were plugged in. The default profile is a USBTMC-USB488 instrument
  — VID `0x1209` PID `0x0001`, serial = the device alias — that the
  vendor VISA runtime lists as `USB0::0x1209::0x0001::<device>::INSTR`,
  answers SCPI from the device's scenario, reports its status byte, and
  raises service requests over the interrupt-IN endpoint. `route add
  --profile cdc-acm` exports the same device as a CDC-ACM serial port
  instead (PID `0x0002`; the inbox serial driver binds and a COM port /
  `/dev/ttyACM*` appears, 115200 8-N-1, one SCPI line per newline). One
  attach per device at a time; a second import while one is up is
  refused. Verified on Windows 11 through usbip-win2 and NI-VISA, and on
  Linux (WSL2) through `vhci-hcd`; a Wireshark loopback capture decodes
  the whole exchange with the built-in `usbip` dissector.
- **A scenario rule can raise a service request** (#138). `srq = <status
  byte>` on a rule (`rule add --srq 0x60`) makes the mock raise one SRQ
  with that status byte, verbatim, whenever the rule fires — after any
  transition, on respond, ack, and fail alike — so the IEEE 488.2 pattern
  (`*ESE 1; *SRE 32; *OPC` → SRQ) is a three-rule scenario. Delivered
  through every gateway that carries SRQs: HiSLIP, VXI-11, USB.
- **Quirk profiles** (#129). A scenario's optional `[quirks]` table asks
  the mock to reproduce a firmware fault; the first quirk,
  `srq_notify_wedge_after = <n>`, stops SRQ notifications after *n*
  deliveries while the status byte keeps recording — the shape a Kikusui
  PWR401L showed on the bench. A restart of the serving process is the
  mock's power cycle.
- **USB instruments in `visa scan`** (#112). Discovery enumerates
  `USB?*::INSTR` through the installed VISA runtime (via the IVI
  Foundation's `IviFoundation.Visa` shared components); without a runtime
  the USB entries are simply absent.
- **Service requests from Local-backend devices reach the gateways**
  (#114). Devices routed through the Local (vendor VISA) backend — USB,
  GPIB, local TCPIP — now deliver SRQs to `ServiceRequestStream`, so a
  HiSLIP or VXI-11 gateway forwards them to remote clients.
- **`ivicli server add --type usbip`** and **`server route add --profile`**
  (#122, #126, #137); `mock scenario show` renders a rule's `srq` (#138).
- **Guide: how `mock` and `server` fit together** (#137). A device is an
  alias, an activated scenario makes the mock answer that device, a
  server routes endpoints to devices without inspecting them — and the
  serving recipes now register the device before binding a scenario to it.
- **Verified instruments moved to `docs/verified-instruments.md`** (#120)
  with a per-transport column layout.

### Changed

- **CLI errors are logged through the contract they carry** (#109). The 24
  command sites that hand-assembled a log call from an error's severity,
  template, arguments, and cause now call `LogIviError`, the way the SOCKET
  gateway has since #102. Log output is unchanged; what is gone is the
  chance of a site quietly dropping the cause or the structured arguments.
- **`mock scene` and `mock rule` are siblings of `mock scenario`** (#22).
  Both were two levels down, under `scenario`, which hid them from
  `ivicli mock --help` — and the `scenario` segment bound nothing, since
  every one of their verbs names its scenario as an argument anyway. So
  `ivicli mock scene add my-dmm idle` and `ivicli mock rule add my-dmm --in
  idle …` are the spellings now, and `ivicli mock --help` shows all three
  nouns. The old `mock scenario scene …` / `mock scenario rule …` paths
  keep working, hidden from help, and **are removed at 0.4.0**.
- **Device names may contain hyphens** (#23). `ivicli visa add psu-mock ...`
  works. `DeviceName` was the only name in the domain that banned them —
  scenario, scene, server, and endpoint names all allowed them — and nothing
  depended on the ban. A rejected name is now told why and what to type
  instead: `invalid device name 'PSU.1': use lowercase letters, digits,
  underscores and hyphens, starting with a letter, at most 64 characters.
  Try 'psu_1'.`
- **Serilog.Sinks.File 7.0.0** (#35). The major bump held back since #29;
  it fixes a force-reopen of the log file every 30 minutes. What the sink
  puts on disk is unchanged — one dated file per day of Compact JSON, one
  event per line — and a test now pins that.

- **System.CommandLine 2.0.11** (#146). The CLI moves from the 2.0
  beta to the stable release; help, completion, and exit codes are
  unchanged.
- **Undelivered service requests are capped** (#139). A device keeps its
  newest 256 requests and drops the oldest when nobody reads them, so a
  scenario raising SRQs into a raw-socket or CDC-ACM gateway no longer
  grows the process without bound.
- **USB/IP detaches are logged** (#144): every attach ends with `device
  <busid> detached (device <name>)`.

### Fixed

- **`session.json` is locked to its owner on Windows too** (#19). ADR 0017
  §4 has always required an NTFS ACL granting only the current account;
  only the Unix half (`chmod 0600`) was implemented, and the Windows file
  simply inherited whatever its directory granted. Measured on a
  domain-joined workstation, that inheritance handed a workstation group
  modify rights. The file's DACL is now protected and carries one entry:
  full control for the account that wrote it.

- **A LAN device's port suffix survived nowhere it was written out**
  (#144). `visa add dut 'TCPIP0::…::hislip0,5000::INSTR'` was saved as
  `hislip0` and dialled on 4880 afterwards; the API's device DTO and the
  string handed to the vendor VISA runtime dropped the same suffix — for
  `gpib0,5`, the instrument's address. All three now write the canonical
  resource string.
- **`CLEAR_FEATURE(ENDPOINT_HALT)` on an exported USB device is
  accepted** (#147) instead of stalled; the Windows USBTMC driver sends
  it at close.
- **VXI-11 broadcast discovery survives Windows UDP resets** (#113). An
  ICMP Port Unreachable from any probed host no longer ends the discovery
  window for that interface, and USB/GPIB resources print once in the
  human scan listing.
- **Two routes of one device no longer race** (#128): a second USB/IP
  import of an instrument already attached is refused cleanly instead of
  resetting mid-enumeration.

### Removed

- **`ivicli diagnose`** — the alias deprecated at 0.2.8 is gone; use
  `ivicli doctor`.

## [0.2.10] — 2026-08-05

### Fixed

- **Concurrent session saves can no longer lose `session.json`** (#106). Two
  processes persisting the session at once — as the mock container's two
  gateway processes do when activating the env-named scenario at startup —
  could leave no session file at all, and a missing file reads as an empty
  session, which deactivates every live scenario binding on the next
  request (the container then answers `*IDN?` with the bare FakeBackend
  response). Saves now write a uniquely named temp file and replace the
  destination in one atomic step, retrying briefly on Windows replace
  contention, and a failed persist of an env-activated binding is logged
  instead of discarded. Measured on native arm64 runners: 3/10 container
  boots hit the race before the fix, 0/10 after.

## [0.2.9] — 2026-08-05

> Published to NuGet only: the release pipeline's arm64 container smoke
> caught the session-save race fixed in 0.2.10, so the GitHub Release and
> container image for 0.2.9 never shipped.

### Fixed

- **SOCKET gateway failures are logged through the error's own contract**
  (#102). Every failure call site now logs the severity, message template,
  structured arguments, and cause the error variant declares, instead of a
  per-site fixed string. In gateway logs, a pool-lease wait ("another op is
  in flight", warning) is now distinguishable from a silent instrument
  (error).

## [0.2.8] — 2026-07-22

### Added

- **Live mock-scenario re-binding on a running gateway** (#85). The SOCKET,
  HiSLIP, and VXI-11 gateways re-sync a device's active scenario from the
  session before each SCPI dispatch, so `ivicli mock scenario activate` run in a
  separate process takes effect on the next request without restarting the
  gateway. Re-application is scoped to a changed scenario name, so in-flight
  scene state is preserved across the frequent reconnects real clients perform.
  `IVICLI_SCENARIO` activation now records its binding in the session so an
  env-activated scenario (e.g. the mock container's) survives the reconciliation.
- **`ivicli mock received <device>`** (#85). Reads the `IVICLI_CAPTURE` traffic
  log out of band to confirm which SCPI writes a device received — for an
  integration test that drives a mock through its own VISA stack and never sends
  raw SCPI itself. `--match <substr>` / `--exact <scpi>` filter the writes,
  `--all` lists every match, `--count` reports the count, and `--json` emits a
  JSON array; the command exits non-zero when nothing matched (except `--count`).
- **Dual license: `MIT OR Apache-2.0`** (#72). `LICENSE-MIT` and `LICENSE-APACHE`
  ship in the repository and the NuGet package, with the SPDX expression as the
  canonical declaration. Contributions are dual-licensed under the same terms.

### Changed

- **`ivicli diagnose` renamed to `ivicli doctor`** (#81). `diagnose` remains a
  deprecated alias for backward compatibility and will be removed at 0.3.0.

## [0.2.7] — 2026-07-01

### Added

- **`visa scan --port <n>`: TCP-sweep for raw-SOCKET instruments** (#65).
  Broadcast/mDNS discovery cannot see a device that speaks SCPI only on a
  raw socket (e.g. a Keithley 2701 on its vendor port 1394). `--port`
  (repeatable) opens a bounded-timeout TCP connection to every host of the
  local `/24`-or-smaller subnets and reports each responder as
  `TCPIP0::<host>::<port>::SOCKET`. APIPA and oversized subnets are skipped;
  `--subnet <cidr>` / `--host <ip>` override the target set.
- **`visa scan`: per-host protocol enrichment** (#65). Every discovered host
  is probed on the well-known instrument ports it did not already surface —
  HiSLIP `4880`, SCPI-RAW `5025`, and any `--port` — so a device found via
  VXI-11 now also lists its HiSLIP and SCPI-RAW access paths. Output is
  grouped by host.
- **`visa scan --verbose`** (#65). Sends `*IDN?` to each open SOCKET
  endpoint to report the instrument model, and shows the VXI-11 Core port
  the portmapper resolved. The Core port stays a diagnostic — the registered
  resource remains the port-less `inst0::INSTR`, re-resolved on each connect,
  because the dynamic port changes across reboots.
- **Explicit port in a TCPIP resource via `lan_device,port`** (#64).
  `TCPIP0::host::inst0,20001::INSTR` (VXI-11) and
  `TCPIP0::host::hislip0,5000::INSTR` (HiSLIP) pin a non-standard Core /
  HiSLIP port, matching the VISA convention NI-VISA and pyvisa emit. Without
  the comma the client resolves the port normally (portmapper for VXI-11,
  4880 for HiSLIP).

### Security

- **Pin Microsoft.OpenApi 2.7.5** to clear NU1903 (GHSA-v5pm-xwqc-g5wc).
  `Microsoft.AspNetCore.OpenApi` 10.0.9 pulled the vulnerable transitive
  2.0.0; 2.7.5 is the first patched 2.x release, transitive-pinned via CPM.

## [0.2.6] — 2026-06-29

### Fixed

- **VXI-11 client: real portmapper round-trip** (closes #20). The
  client connected straight to a fixed port (1024) and timed out
  against physical instruments that assign the VXI-11 Core to a
  dynamic port. `OpenAsync` now issues a `PMAPPROC_GETPORT` over
  **UDP/111** to resolve the Core port, falling back to the fixed
  port when no portmapper answers (e.g. ivi-cli's co-located gateway,
  which does not answer GETPORT on 111). Verified against a Kikusui
  PWR801L: its portmapper answers GETPORT only over UDP (TCP/111
  accepts connections but never replies). Note: on that unit the
  advertised Core port is not reachable, so VXI-11 `*IDN?` still
  cannot complete there — a device-side limitation, independent of
  the client; its SOCKET (5025) and HiSLIP (4880) paths are unaffected.
- **`visa scan`: discover instruments on every interface.** On a
  multi-homed host the scanner sent its portmapper probe only to the
  limited broadcast `255.255.255.255`, which egresses a single NIC, so
  instruments on a secondary lab subnet were never found. It now
  enumerates every operational IPv4 interface and sends a
  subnet-directed broadcast (e.g. `192.168.3.255`) bound to each NIC,
  aggregating responders. Verified: discovery of a real instrument on
  a secondary subnet went from 0 to found.
- **`visa scan`: show the real host.** Discovered resources are now
  printed unmasked (real host) in human and `--json` output instead of
  the `***`-masked log form, matching what `--add` writes to config
  (`ToLogString` masking is scoped to logging, ADR 0017).

### Added

- **`visa list` shows the resource string.** Human output is now
  `name<TAB>resource<TAB>timeout`, and `--json` gains a `resource`
  field, so the host/port/protocol behind each alias is visible without
  reading `config.toml`. Backed by a new `VisaResource.ToCanonical()`
  (the unmasked inverse of `Parse`), reused by `visa scan`.

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
