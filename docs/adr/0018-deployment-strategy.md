# 0018. Deployment Strategy

- Status: Accepted
- Date: 2026-05-29

## Context

Phase 3 (Batches L–U) shipped the gateway-server stack, Management
API, mock scenario system, audit log, plugin loader, and PAT
scopes/expiry. Operators, contributors, and **3rd-party VISA-app
developers running e2e tests** now need a way to *acquire* and
*run* ivi-cli that matches their environment. The pre-existing
release.yml pipeline produced self-contained binaries (6 RIDs) and
a `dotnet tool` nupkg, but there was no container channel — and
the container is the dominant deployment shape for the e2e-mock
use case that Phase 3 unlocked.

This ADR consolidates the deployment-channel inventory and adds
the new container channel as the centerpiece. The mock-VISA
container makes it trivial for an external developer to spin up a
scriptable VISA instrument with `docker run` and point their app
under test at it — no real hardware required.

The architectural story that motivates the container shape:

```
[3rd-party app under e2e test]
            │  TCP / VISA (HiSlip 4880 or SOCKET 5025)
            ▼
   [ Mock VISA container ]              ← Batch V deliverable
   ghcr.io/<owner>/ivi-cli-mock
   (ivicli server start + scenario-backed FakeBackend)
            ▲
            │  Same TCP / VISA protocols
            │
[ ivicli visa query / write / read ]    ← unchanged CLI
            │
            ▼
   [ Real VISA instrument ]
```

The same `ivicli` binary fills three personas with a single build
output:

- The container *is* the gateway side (gateway server + mock
  backend).
- The CLI *is* the client side (`ivicli visa query` against the
  container or against real hardware — same wire protocol).
- The CLI is also the **runtime mock-control plane**
  (`docker exec mock ivicli mock scene add ...`) for the
  unit-test-style "arm before each assertion" workflow that the
  static scenario file does not cover.

## Decision

### 1. Distribution channels

| Channel | Audience | Source of truth |
| --- | --- | --- |
| Self-contained single-file binary | Lab operators on Windows / Linux / macOS, 6 RIDs (linux-x64/arm64, win-x64/arm64, osx-x64/arm64) | release.yml `publish` job |
| `dotnet tool` nupkg | .NET ecosystem users (`dotnet tool install -g ivicli`) | release.yml `pack` job |
| **Container image (NEW, Batch V)** | **3rd-party VISA-app e2e test mocks; CI pipelines** | **release.yml `docker` job** |
| Source build | Contributors | CONTRIBUTING.md |

All channels are produced by the same release.yml pipeline on
`v[0-9]+.[0-9]+.[0-9]+*` tag push, version-locked to the same
git SHA.

### 2. Container persona — mock-VISA appliance

The container ships **only the gateway server + scenario-backed
mock**. The Management API is intentionally not exposed. This
keeps:

- Image surface small (no inbound HTTPS, no PAT auth path).
- Purpose unambiguous (operators reading `docker pull` know what
  this image does).
- Per-test isolation easy (spin up multiple containers, no API
  shared state).

Runtime mock control during a test session is achieved via:

```
docker exec <container> ivicli mock scene add demo \
    --match "*MEAS?" --respond "1.234"
```

This re-uses every CLI mock verb wired up in Batches A–U (the
audit-log wiring in Batch U covers these emissions). Adding HTTP
endpoints for mock CRUD over the Management API is **deferred** —
`docker exec` covers the v1 use case with zero new code.

### 3. Protocols exposed

The container starts three gateway servers in parallel:

| Protocol | Port | TCP shape | Audience |
| --- | --- | --- | --- |
| HiSlip | 4880 | Single TCP | NI-VISA, IVI.NET, Keysight VISA — modern VISA standard |
| SOCKET | 5025 | Single TCP | lxi-tools, raw socket clients, simple shell pipelines |
| VXI-11 | 111 (+ 111/udp) | Single TCP + UDP portmap | legacy VISA clients, pyvisa-py, ivicli's own VXI-11 backend |

All three map cleanly to Docker port forwarding. VXI-11 was
deferred in v1 on the assumption that its portmapper hands out
dynamic ports; the gateway never worked that way — it multiplexes
portmap, Core, and abort RPCs on its single TCP port and answers
GETPORT with that same port (issue #14, resolution (b)). Binding
the gateway to 111 inside the container makes the answered port
identical on both sides of the mapping, so
`-p 111:111 -p 111:111/udp` is the whole setup. The UDP mapping
serves clients that resolve the Core port over UDP — the only
transport some embedded portmappers answer, and the one ivicli's
client and broadcast scanner use.

One limit stays: subnet-broadcast discovery (`visa scan`) cannot
reach a bridge-networked container, because broadcasts are not
DNATed. Operators who want the mock discoverable by broadcast run
it with `--network host` (Linux); everything else works
port-mapped.

### 4. Image construction

- Base: `mcr.microsoft.com/dotnet/runtime-deps:9.0-bookworm-slim`
  (Debian glibc). Provides the native deps a self-contained .NET
  binary needs (libc, libssl, libgcc, libstdc++, tzdata, ca-certs)
  without bundling a separate .NET runtime layer — the runtime
  is already inside the self-contained publish output. Pinned to
  the `9.0` base tag because the `10.0` tag is not yet published
  by Microsoft; the base-image .NET version is independent of the
  ivi-cli .NET version (it ships its own).
- Self-contained binary: re-uses release.yml's `linux-x64` and
  `linux-arm64` publish outputs. No SDK image, no second build —
  the container is purely packaging.
- Multi-arch: `linux/amd64` + `linux/arm64` via docker buildx
  manifest list. Build inputs come from the two existing publish
  artifacts (Apple Silicon dev + AWS Graviton / Ampere CI both
  get native binaries).
- Bash + netcat-openbsd installed via apt for the entrypoint shell
  and HEALTHCHECK probe.
- Image size: ~334 MB (debian-slim base ~150 MB + self-contained
  binary ~102 MB + apt layer ~80 MB). NativeAOT trimming could
  cut this further but is deferred — ivi-cli's dependency stack
  (Serilog, OpenTelemetry, ASP.NET Core, Tomlyn, plugin
  reflection) requires AOT-compat audit first.

### 5. Pre-baked config vs mount

The image ships a default `/etc/ivi-cli/config.toml` and one
sample scenario under `/etc/ivi-cli/scenarios/default.toml`. The
defaults wire two `[[servers]]` (HiSlip 4880, SOCKET 5025) backed
by a single `[[devices]]` entry routed via `[[routes]]`. A
minimal scenario covers `*IDN?` / `*RST` / `*OPC?` / `SYST:ERR?`
so `docker run … && curl-equivalent SCPI` works without any
mount.

Operators override either layer:

- `-v ./my-config.toml:/etc/ivi-cli/config.toml` — custom servers
  / devices / routes.
- `-v ./my-scenarios:/etc/ivi-cli/scenarios` — replace the
  scenario directory.
- `-e IVICLI_CONFIG=/data/cfg.toml` — point at a different config
  path.
- `-e IVICLI_SCENARIO=otherone` — activate a different scenario
  at startup.

### 6. ENTRYPOINT + HEALTHCHECK

ENTRYPOINT is a small bash script that:

1. Traps SIGTERM / SIGINT and forwards to children.
2. Starts `ivicli server start hislip-mock &` (PID captured).
3. Starts `ivicli server start socket-mock &` (PID captured).
4. `wait -n` on the children; any exit propagates a TERM to the
   other and exits with the child's status.

Verified: `docker stop` returns in ~1.6 s well within the default
10-s grace period. No supervisord or s6 layer needed; PID 1 is
the entrypoint shell.

HEALTHCHECK uses `nc -z 127.0.0.1 4880 && nc -z 127.0.0.1 5025`
(busybox netcat-openbsd) — TCP probe only. The gateway listeners
accept the TCP connect within milliseconds of startup; the
healthcheck does not issue a SCPI query (per-check device
configuration would be needed and the marginal value is low).

### 7. Registry + tag scheme

- Registry: `ghcr.io/<owner>/ivi-cli-mock`. Auth via the
  workflow's built-in `${{ secrets.GITHUB_TOKEN }}` — release.yml
  already declares `permissions.packages: write`. No new secret
  is required for v1. Operators who pull anonymously hit ghcr.io
  with no rate limit (Docker Hub mirror deferred).
- Tags per release:
  - `vX.Y.Z` (immutable, points to the tagged commit)
  - `vX.Y` (minor pin — moves when a new patch ships in the same
    minor series)
  - `sha-<7>` (immutable, for absolute pinning in CI)
  - `latest` (moves with every release; demo / hello-world only,
    not for production pins)

### 8. Runtime mock control

For unit-test-style "arm before each assert" flows, tests run:

```
docker exec mock ivicli mock scenario create per-test
docker exec mock ivicli mock scene add per-test \
    --match "*MEAS?" --respond "1.234"
docker exec mock ivicli mock scenario activate per-test
```

This reuses every CLI mock verb already wired through Batch U's
audit log. The pattern requires the test framework to have Docker
socket access, which is typical in CI runners but not always
available. A future batch may add Management API endpoints for
HTTP-based mock CRUD if external demand surfaces — see
[ADR 0026](0026-mock-scenario-system.md) for the underlying mock
state model.

### 9. Smoke gates

Three smoke gates protect the image quality:

1. **Local** (developer-side, before commit): the contributor runs
   `dotnet publish … -r linux-x64 … && docker build … && docker run …
   && docker exec mock-test bash -c 'echo "*IDN?" | nc -w 2 127.0.0.1 5025'`
   per the verification block of the Batch V plan. Manual,
   pre-PR.
2. **PR-time** (`.github/workflows/pr-docker-smoke.yml`): paths-
   filtered to PRs touching `docker/**`, `.dockerignore`, or
   `src/IviCli.Cli/**`. Publishes, builds single-arch (amd64),
   runs HEALTHCHECK + SCPI roundtrip. No push. Status check
   appears only on relevant PRs.
3. **Release-time** (`.github/workflows/release.yml`): native
   smoke gates on **both architectures** — amd64 inside the
   `docker` job, arm64 in `docker-smoke-arm64` on an arm64 runner
   — **before** the multi-arch push. Failure of either aborts the
   push so `latest` is never updated to a broken image. After both
   smokes pass, buildx pushes the multi-arch manifest with all
   four tags. All three gates share `docker/smoke-test.sh`
   (HEALTHCHECK + SCPI round-trip).

### 10. Required runtime composition env

The container sets these envs in the Dockerfile so a bare
`docker run` works without operator intervention:

| Env | Value | Effect |
| --- | --- | --- |
| `IVICLI_CONFIG` | `/etc/ivi-cli/config.toml` | Use the baked config. |
| `IVICLI_MOCK_ONLY` | `1` | Collapse every transport-specific backend (Local / HiSlip / Socket / Vxi11) to the FakeBackend fallback. Without this the gateway would try to make outbound transport connections instead of replying from the scenario. |
| `IVICLI_SCENARIO` | `default` | Activate the baked scenario on FakeBackend at process startup. |

`IVICLI_MOCK_ONLY` is a small composition-root override in
`src/IviCli.Cli/Program.cs` introduced by Batch V. It's an env
contract; no CLI flag is exposed (containers set env, humans rarely
need the bypass).

### 11. NativeAOT flavor (issue #15)

The audit happened (buckets on the issue: JSON and TOML moved to
source-generated contexts, the plugin loader sits behind the
`IviCli.Plugins.IsSupported` feature switch, and the trim/AOT
analyzers run on every build). On top of it, `docker/Dockerfile.aot`
builds a NativeAOT variant of this image: a multi-stage build whose
SDK stage runs `dotnet publish -p:MockContainerAot=true` for the
RID it maps from BuildKit's `TARGETARCH` — the flavor property on
`IviCli.Cli.csproj` that turns on
`PublishAot`, `InvariantGlobalization`, `StripSymbols`, pins the
plugin switch off so the trimmer deletes the loader, and demotes
exactly four third-party ILC warning codes (`Ivi.Visa`,
`Common.Logging`, one ASP.NET `ModelMetadata` member) while every
other ILC finding still fails the publish. NativeAOT cannot
cross-compile between OSes, so the publish lives inside the image
build rather than in the host-side artifacts flow the JIT image uses.

Measured on 2026-08-21 (amd64, `docker save | gzip | wc -c` for the
compressed figures, both images built from the same commit):

| | JIT image | AOT image |
| --- | --- | --- |
| compressed | 142.7 MB | **58.1 MB** |
| uncompressed (`docker images`) | 493 MB | 217 MB |
| app binary | 106.4 MB single-file bundle | 25.0 MB native |
| in-container `ivicli --version`, 20 runs | 234 ms/run | **13.3 ms/run** |

The issue's `< 30 MB compressed` target is not reachable on this
base image: the debian rootfs plus the runtime-deps ICU/OpenSSL
layer cost 47.1 MB compressed before any ivi-cli bytes, and the app
itself is only 11.4 MB of the 58.1. Getting under 30 MB means a
chiseled or alpine base, and both break the bash entrypoint and the
`nc` healthcheck — a rewrite this ADR does not take on. Two smaller
follow-ups are recorded here instead: `InvariantGlobalization` makes
the 18.7 MB ICU layer dead weight (a plain `debian:bookworm-slim`
base would drop it), and `Ivi.Visa` + `Common.Logging` survive
trimming because `IviCli.Backends.Local` is registered in DI
unconditionally — `IVICLI_MOCK_ONLY` stops the calls, not the
static reachability.

The flavor is CI-gated on every container-touching PR
(pr-docker-smoke builds it and runs the same smoke as the JIT image,
hadolint lints it) and **published** alongside the JIT image.

Tagging suffixes the JIT scheme rather than forking the package:
`vX.Y.Z-aot` and `sha-<sha7>-aot` on every release tag, plus
`vX.Y-aot` and the floating `aot` on a final release only — a
pre-release moves neither floating tag, exactly as it moves neither
`latest` nor `vX.Y`. The default (unsuffixed) tags stay JIT, so an
operator opts into AOT explicitly and nobody's `docker pull` changes
flavor under them.

The build cannot use the JIT image's buildx-with-QEMU path: ILC
neither cross-compiles nor runs at tolerable speed under emulation.
So `release.yml` builds `docker/Dockerfile.aot` natively on
`ubuntu-latest` and on `ubuntu-24.04-arm`, smoke-tests each
single-arch image on its own architecture, pushes the two as
`sha-<sha7>-aot-<arch>`, and stitches them with
`docker buildx imagetools create`. imagetools writes an index over
manifests already in the registry, so the bytes that pass the smoke
are the bytes the index points at. The manifest job gates the GitHub
Release, which makes a failed AOT publish a failed release rather
than a half-shipped one.

`InvariantGlobalization` is accepted for this image. The mock's
surface is ASCII SCPI, and the codebase already compares ordinally
and formats invariantly throughout, so there is no culture-sensitive
behaviour for the switch to change — it removes a capability nothing
here uses.

The `runtime-deps` base is kept deliberately, and with it the
18.7 MB ICU layer that `InvariantGlobalization` makes dead weight.
This image is the same product as the JIT one, and an operator who
mounts a config turning on TLS or the Management API has to find
libssl and tzdata where the JIT image put them. Dropping to
`debian:bookworm-slim` would trade that parity for ~19 MB and turn
one runtime configuration into a flavor-dependent failure.

#### What the AOT flavor deliberately does not cover

AOT stops at the mock container. The CLI in the release archives
stays a single-file self-contained JIT publish, which is a decision
and not work left undone.

The Local backend reaches an instrument through whatever VISA
implementation the machine has installed, and VISA.NET locates that
implementation at runtime. Our own builds name the members:
`Ivi.Visa.Internal.VisaAssemblyLoadContext.Load` calls
`AssemblyLoadContext.LoadFromAssemblyPath` (IL2026),
`ResourceManagerFactory.TryCreateResourceManager` and
`ConflictManager.GetConflictManagerApi` resolve the vendor type from
a composed name through `Type.GetType` (IL2057), and
`VisaAssemblyLocator.GetApplicationLocalVendorAssembliesDirectory`
reads `Assembly.Location` (IL3000). NativeAOT has no runtime assembly
loading, so an AOT build of the CLI has no vendor implementation to
find and every Local-backend resource fails.

The failure mode is why this is worth writing down rather than
leaving to whoever reads the flavor property next. The publish
succeeds: those diagnostics come from a third-party assembly, and the
flavor demotes exactly them. Nothing breaks until a user points
`visa scan` or a USB resource at real hardware and gets nothing back.
Demoting the warnings is correct for an image that answers from
`FakeBackend` on every transport (`IVICLI_MOCK_ONLY=1` collapses them
before VISA is reached) and misleading for a CLI whose purpose is to
talk to instruments.

`PublishAot` therefore stays behind `MockContainerAot`, and the
per-RID archives keep shipping the single-file JIT binary — the one
that installs on a machine with no .NET on it.

## Consequences

- **3rd-party VISA-app e2e tests become a `docker run` away.** A
  single command spins up a scriptable mock instrument with no
  hardware dependency, no .NET install, no manual config.
- **The CLI is dog-fooded as both server and client.** The same
  binary that runs in the container is the binary the test fleet
  uses to probe it (`ivicli visa query`). Failures surface in
  both personas simultaneously.
- **Release pipeline grows by one job** (`docker`) between
  `publish` and `release`. CI minutes per release rise by
  ~3–5 min (multi-arch buildx + smoke). PR pipeline impact is
  ~0 for 99 % of PRs thanks to the paths filter.
- **Image size** is larger than an alpine + AOT optimum. v1 traded
  size for compatibility, and the audit that unblocked the trade
  has since happened: the AOT flavor in §11 ships the same mock at
  58.1 MB compressed against the JIT image's 142.7 MB, on the same
  base and with the same capabilities.

## Out of scope (v1)

- ~~**VXI-11 in container**~~ — shipped (§3): the gateway's
  single-port portmap+Core design maps with `-p 111:111
  -p 111:111/udp`; only broadcast discovery still needs
  `--network host`.
- ~~**NativeAOT image**~~ — shipped (§11 NativeAOT flavor):
  published to ghcr.io under `-aot`-suffixed tags, built
  natively per architecture and stitched into one index.
- **NativeAOT for the distributed CLI** — the Local backend needs
  the runtime assembly loading NativeAOT does not have, and the
  breakage would surface on a user's bench rather than in the build
  (see "What the AOT flavor deliberately does not cover").
- **Docker Hub mirror** — ghcr.io only for v1. Mirror when an
  external operator reports the lack of Docker Hub presence.
- **Windows container base** — image > 1 GB, no e2e mock value
  (Windows e2e clients reach a Linux container over TCP just
  fine).
- **Kubernetes Helm chart / operator** — downstream concern; not
  required for the e2e-mock use case.
- **Per-test isolated namespacing inside one container** —
  handled at the test-framework level (spawn N containers, each
  with its own scenario).
- **Multi-server CLI flag** (`ivicli server start --all`) — the
  shell entrypoint suffices for v1; a CLI extension is a future
  ergonomic improvement if useful outside the container too.
- **Management API mock-control endpoints over HTTP** —
  `docker exec` + existing CLI verbs cover the v1 need; ADR 0026
  follow-up if HTTP-only control is requested.
- **Image signing / SBOM** — out of scope for v1; can layer
  cosign / SLSA into the release job in a future batch.

## Verification

- **Local** (Batch V Task 1): documented in the Batch V plan.
  Build, run, healthcheck, SCPI roundtrip, graceful stop.
- **PR-time** (Batch V Task 3): create a no-op PR touching
  `docker/Dockerfile` (e.g. whitespace) and confirm the
  `docker-smoke` workflow runs. Create a PR touching only a
  domain test and confirm it does NOT run.
- **Release-time** (Batch V Task 2): the first `v*` tag push
  after Batch V will exercise the smoke gate end-to-end. If the
  image smoke fails, the multi-arch push is skipped and the
  GitHub Release is not created — a clean failure mode.
- **End-to-end e2e** (downstream consumer's POV): pull the
  published image, point a SCPI client at `localhost:4880` or
  `localhost:5025`, send `*IDN?`, expect
  `IVICLI-MOCK,gateway,1,0.1.0`.
