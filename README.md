**English** | [日本語](docs/README.jp.md)

# ivi-cli

`ivi-cli` is an integrated CLI for managing, diagnosing, and operating instruments addressed via VISA/IVI.

> Status: **v0.1.0 — first public release.** Phase 1–3 are landed: CLI core, HiSLIP / VXI-11 / SOCKET gateways, scenario-driven mock-VISA container (ghcr.io/shortarrow/ivi-cli-mock), Management HTTP / WebSocket API with PAT + TLS + audit, OpenTelemetry, and LXI mDNS / VXI-11 broadcast discovery. Pre-1.0.0, breaking changes are still possible per [ADR 0022](docs/adr/0022-branching-strategy.md). See [CHANGELOG.md](docs/CHANGELOG.md).

## Highlights

- **Stateful, VISA-native CLI**
  - **Stateful UX.** Register an alias once with `ivicli visa add psu1 ...`; subsequent commands operate on `psu1` without retyping the VISA resource.
  - **VISA-compatible.** Parses standard `TCPIP::`, `USB::`, `GPIB::` resource strings without proprietary syntax.
  - **Automation-friendly.** Stdout carries data (including `--json`); stderr carries logs. Exit codes are POSIX-conventional. Shell completion ships for bash / zsh / PowerShell.
- **Discover & inspect**
  - **Auto-discovery.** `ivicli visa scan` walks the LAN via LXI mDNS / DNS-SD + VXI-11 portmapper broadcast and lists every responder; `--add` registers them all in one shot.
  - **IVI Configuration Store introspection.** `ivicli driver list` / `ivicli logical list` parse `IviConfigurationStore.xml` to enumerate installed IVI drivers and logical names — debugging "instrument talks but driver mismatched" without opening the Configuration Server GUI ([ADR 0045](docs/adr/0045-ivi-configuration-store.md)).
- **Backends & gateways**
  - **Multiple backends.** Local NI-VISA, HiSLIP, VXI-11, raw TCP SOCKET, Fake (programmable + scenario playback), Replay (strict deterministic playback) — all behind a single `IIviBackend` port.
  - **Gateway servers.** Expose a local instrument over HiSLIP (`TCPIP::host::hislip0::INSTR`) or raw socket so remote PyVISA / NI-VISA clients can drive it without redeploying the test.
- **Test without hardware**
  - **Recordable scenarios.** `mock scenario record --from-script` captures the SCPI traffic of a script run; `IVICLI_REPLAY=<scenario>` re-runs the same scripts deterministically without hardware.
  - **Record once, replay forever.** Capture a real session with `IVICLI_CAPTURE`, convert it via `mock scenario import`, then drive any verb with `IVICLI_REPLAY=<name>` — no more hardware time burned on regression checks.
  - **Lint your scripts.** `visa lint foo.scpi` catches unknown SCPI roots (IEEE 488.2 + SCPI core) before you run them, without touching the instrument.
  - **Audit-friendly.** Set `IVICLI_CAPTURE=<path>` and every backend operation streams to an NDJSON log for post-hoc inspection — `tail -f path | jq` or hand it to support.
- **Control plane (HTTP / WebSocket API)**
  - **JSON HTTP API.** `ivicli api start` exposes a JSON HTTP API at `http://127.0.0.1:8080/v1` (with `/openapi/v1.json`) so AI agents, dashboards, and CI scripts can list devices / fire SCPI queries / read status without speaking VISA.
  - **Browser-friendly streaming.** A WebSocket at `ws://127.0.0.1:8080/v1/devices/{name}/visa` carries `{op:'query',scpi:'…'}` frames and replies with `{event:'response',…}` — drop-in for any dashboard or AI agent runtime (ADR 0035).
  - **Lock down the API.** `ivicli api token create` mints a PAT (shown once, only the hash is stored); the listener validates `Authorization: Bearer …` for HTTP and `ivi-cli-pat.<token>` for WebSocket so binding beyond loopback is safe (ADR 0036).

## Install

```sh
# .NET tool (requires the .NET 10 SDK or runtime)
dotnet tool install -g ivi-cli

# Self-contained single-file binary (no .NET install required)
# Download the artifact for your OS / arch from the GitHub Releases page.
```

Releases ship for `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, and `osx-arm64`.

## Quick start

```sh
# 1. Register an instrument
ivicli visa add psu1 TCPIP0::192.168.0.10::inst0::INSTR
ivicli visa use psu1

# 2. Talk to it
ivicli visa query "*IDN?"
ivicli visa write "OUTP ON"

# 3. Replay a recorded scenario instead of hitting hardware
IVICLI_REPLAY=psu1-smoke ivicli visa query "*IDN?"

# 4. Watch every registered instrument live (Ctrl+C to exit)
ivicli visa watch --interval 500

# 5. Expose the instrument over HiSLIP for remote clients
ivicli server add hislip-srv --type hislip --port 4880
ivicli server route add hislip-srv hislip0 psu1
ivicli server start hislip-srv
```

Configuration lives at the platform-specific XDG-style path:

| OS | Default path |
| --- | --- |
| Linux | `$XDG_CONFIG_HOME/ivi-cli/config.toml` (default `~/.config/ivi-cli/config.toml`) |
| macOS | `~/.config/ivi-cli/config.toml` |
| Windows | `%LOCALAPPDATA%\ivi-cli\config.toml` |

Override with the `IVICLI_CONFIG` environment variable.

## Try it now — no hardware

A ready-made mock instrument in one command — no .NET install, no config:

```sh
docker run --rm -p 4880:4880 -p 5025:5025 \
    ghcr.io/shortarrow/ivi-cli-mock:latest

# In another terminal — using ivicli itself, or any SCPI client:
ivicli visa add mock TCPIP::localhost::hislip0::INSTR
ivicli visa query mock "*IDN?"
# → IVICLI-MOCK,gateway,1,0.1.0
```

The container serves the same scenario over a HiSLIP gateway on `4880` and a raw SOCKET gateway on `5025` (`*IDN?` / `*RST` / `*OPC?` / `SYST:ERR?` out of the box).

## Mock a VISA instrument

Building an app that drives a VISA instrument and want to test it without the hardware on the bench? Stand up a mock that answers your app's SCPI:

- **Run a ready-made mock** — the container above, or the bare CLI.
- **Author a scenario for *your* instrument** — map its `*IDN?`, queries, and state transitions.
- **Point your app at it** — `ivicli` or any VISA client; NI-VISA / Keysight-VISA apps register the mock in NI MAX.

→ **[Mock a VISA instrument](docs/guides/mock-a-visa-instrument.md)** is the step-by-step guide; the **[PSU sample](docs/samples/psu/)** is a complete worked example (drop-in scenario + setup scripts).

## Subcommand map

| Group | Verbs | Purpose |
| --- | --- | --- |
| `visa` | `add` `remove` `list` `use` `current` `scan` `query` `write` `read` `status` `script` `monitor` `watch` `lint` | Manage and talk to instruments |
| `mock scenario` | `list` `create` `remove` `show` `activate` `deactivate` `record` `import` + `scene add` / `scene remove` | Author and capture mock-device scenarios |
| `server` | `add` `remove` `list` `route add` / `route remove` / `route list` `start` `stop` `status` `log` | Gateway-server lifecycle |
| `api` | `start` `stop` `token create` `token list` `token revoke` | Management HTTP JSON API (ADR 0034) + WebSocket subprotocol (ADR 0035) + PAT auth (ADR 0036) |
| top-level | `diagnose` `completion <shell>` | Environment health + shell autocomplete |

## Verbosity & format flags

| Flag | Effect |
| --- | --- |
| (none) | Information+ |
| `-v`, `--verbose` | Debug+ |
| `-vv` | Trace+ |
| `-q`, `--quiet` | Suppress console below Warning (file sink unaffected) |
| `--log-file <path>` | Override the rolling log file destination |
| `--log-format human\|json` | Console format (default `human`) |

## Shell completion

```sh
# bash: source from .bashrc
eval "$(ivicli completion bash)"

# zsh: source from .zshrc
eval "$(ivicli completion zsh)"

# PowerShell: source from your profile
ivicli completion powershell | Out-String | Invoke-Expression
```

Once installed, `<Tab>` expands subcommands, options, and runtime identifiers (device aliases, server names, scenario names).

## Architecture

```mermaid
flowchart LR
    Cli["IviCli.Cli<br/>(composition root)"] --> App["IviCli.Application<br/>(handlers, ports)"]
    Cli --> Server["IviCli.Server<br/>(HiSLIP / SOCKET gateways)"]
    Server --> App
    Cli --> Infra["IviCli.Infrastructure<br/>(TomlConfigStore, FilePidRegistry)"]
    Infra --> App
    Cli --> Backends["IviCli.Backends.*<br/>(Fake / Local / HiSlip / Vxi11 / Socket / Replay)"]
    Backends --> App
    App --> Domain["IviCli.Domain<br/>(value objects, entities, errors)"]
    Server --> Domain
    Backends --> Domain
```

Dependency direction is one-way (Domain ← Application ← {Infrastructure, Backends, Server} ← Cli). The architecture-test suite (`tests/IviCli.Cli.Tests/Architecture/`) enforces it on every PR.

## Documentation

- [PRD](docs/PRD.md) — full product requirements ([日本語](docs/PRD.jp.md))
- [Architecture Decision Records](docs/adr/) — every Accepted decision behind the implementation. Start with [ADR 0003](docs/adr/0003-architecture-style.md) (architecture style), [ADR 0021](docs/adr/0021-repository-layout.md) (layer assemblies), [ADR 0007](docs/adr/0007-network-transport.md) (HiSLIP / SOCKET).
- [Domain glossary](docs/domain-glossary.md) — the ubiquitous-language catalog
- [Guides](docs/guides/) — task-oriented how-tos, starting with [Mock a VISA instrument](docs/guides/mock-a-visa-instrument.md)
- [Samples](docs/samples/) — drop-in scenarios + setup scripts (e.g. [PSU mock VISA device](docs/samples/psu/))
- [Contributing](docs/CONTRIBUTING.md) — local dev loop, branching, hooks ([日本語](docs/CONTRIBUTING.jp.md))

## Building from source

```sh
dotnet tool restore
dotnet restore --locked-mode
dotnet build
dotnet test --filter "Category!=Integration"
```

Local hooks (CSharpier formatter check on commit, build + tests on push) install on first contributor run via `dotnet husky install`.

## License

License TBD. Until a `LICENSE` file is committed, treat the source as "all rights reserved" — open an issue if you need clarification before reusing.
