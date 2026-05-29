# Changelog

All notable changes to ivi-cli are documented here. Format roughly follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning follows
[Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html).

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

[0.1.0]: https://github.com/ShortArrow/ivi-cli/releases/tag/v0.1.0
