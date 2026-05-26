**English** | [日本語](README.jp.md)

# ivi-cli

`ivi-cli` is an integrated CLI for managing, diagnosing, and operating instruments addressed via VISA/IVI.

> Status: **alpha** — Phase 1–3 land, batch C in flight. The CLI builds, ships its full subcommand tree (including a HiSLIP/SOCKET gateway), and persists scenarios; breaking changes possible before v0.1.0.

## Highlights

- **Stateful UX.** Register an alias once with `ivicli visa add psu1 ...`; subsequent commands operate on `psu1` without retyping the VISA resource.
- **VISA-compatible.** Parses standard `TCPIP::`, `USB::`, `GPIB::` resource strings without proprietary syntax.
- **Multiple backends.** Local NI-VISA, HiSLIP, raw TCP SOCKET, Fake (programmable + scenario playback), Replay (strict deterministic playback) — all behind a single `IIviBackend` port.
- **Gateway servers.** Expose a local instrument over HiSLIP (`TCPIP::host::hislip0::INSTR`) or raw socket so remote PyVISA / NI-VISA clients can drive it without redeploying the test.
- **Recordable scenarios.** `mock scenario record --from-script` captures the SCPI traffic of a script run; `IVICLI_REPLAY=<scenario>` re-runs the same scripts deterministically without hardware.
- **Automation-friendly.** Stdout carries data (including `--json`); stderr carries logs. Exit codes are POSIX-conventional. Shell completion ships for bash / zsh / PowerShell.

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

# 4. Expose the instrument over HiSLIP for remote clients
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

## Subcommand map

| Group | Verbs | Purpose |
| --- | --- | --- |
| `visa` | `add` `remove` `list` `use` `current` `scan` `query` `write` `read` `status` `script` `monitor` | Manage and talk to instruments |
| `mock scenario` | `list` `create` `remove` `show` `activate` `deactivate` `record` + `scene add` / `scene remove` | Author and capture mock-device scenarios |
| `server` | `add` `remove` `list` `route add` / `route remove` / `route list` `start` `stop` `status` `log` | Gateway-server lifecycle |
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
    Cli --> Backends["IviCli.Backends.*<br/>(Fake / Local / HiSlip / Socket / Replay)"]
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
- [Contributing](CONTRIBUTING.md) — local dev loop, branching, hooks ([日本語](CONTRIBUTING.jp.md))

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
