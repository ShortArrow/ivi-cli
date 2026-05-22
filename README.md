**English** | [日本語](README.jp.md)

# ivi-cli

`ivi-cli` is an integrated CLI for managing, diagnosing, and operating instruments addressed via VISA/IVI.

> Status: **alpha** — Phase 1 is under active development. The CLI runs and persists configuration, but most subcommands and Backend implementations are still landing. Expect breaking changes before v0.1.0.

## Highlights

- **Stateful UX.** Register an alias once with `ivicli visa add psu1 ...`; subsequent commands operate on `psu1` without retyping the VISA resource.
- **VISA-compatible.** Parses standard `TCPIP::`, `USB::`, and `GPIB::` resource strings without proprietary syntax.
- **Backend-agnostic.** Local NI-VISA, HiSLIP, raw socket, and fake/replay backends share one transport abstraction.
- **Automation-friendly.** Stdout carries data (including `--json` output); stderr carries logs. Exit codes are POSIX-conventional.

## Install

Phase 1 distributes via two channels:

```sh
# .NET tool (requires the .NET 10 SDK or runtime)
dotnet tool install -g ivi-cli

# Self-contained single-file binary (no .NET install required)
# Download the artifact for your OS / arch from the GitHub Releases page.
```

Releases ship for `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, and `osx-arm64`.

## Quick start

```sh
ivicli visa add psu1 TCPIP0::192.168.0.10::inst0::INSTR
ivicli visa list
ivicli visa list --json
```

Configuration lives at the platform-specific XDG-style path:

| OS | Default path |
| --- | --- |
| Linux | `$XDG_CONFIG_HOME/ivi-cli/config.toml` (default `~/.config/ivi-cli/config.toml`) |
| macOS | `~/.config/ivi-cli/config.toml` |
| Windows | `%LOCALAPPDATA%\ivi-cli\config.toml` |

Override with the `IVICLI_CONFIG` environment variable or `--config <path>` (future).

## Verbosity & format flags

| Flag | Effect |
| --- | --- |
| (none) | Information+ |
| `-v`, `--verbose` | Debug+ |
| `-vv` | Trace+ |
| `-q`, `--quiet` | Suppress console below Warning (file sink unaffected) |
| `--log-file <path>` | Override the rolling log file destination |
| `--log-format human\|json` | Console format (default `human`) |

## Documentation

- [PRD](docs/PRD.md) — full product requirements ([日本語](docs/PRD.jp.md))
- [Architecture Decision Records](docs/adr/) — every Accepted decision behind the implementation
- [Domain glossary](docs/domain-glossary.md) — the ubiquitous-language catalog

## Project structure

```
src/
 ├─ IviCli.Domain          — value objects, entities, errors (no external deps)
 ├─ IviCli.Application     — use-case handlers, ports
 ├─ IviCli.Infrastructure  — TomlConfigStore and other adapters
 ├─ IviCli.Backends.Local  — NI-VISA backend (in progress)
 ├─ IviCli.Backends.Fake   — in-memory backend for tests / CI
 └─ IviCli.Cli             — composition root (System.CommandLine, Serilog, DI)
tests/
 ├─ IviCli.<Layer>.Tests   — unit + architecture tests, mirroring src
 └─ IviCli.TestKit         — Test Data Builders, FakeConfigStore, custom assertions
```

See [ADR 0021](docs/adr/0021-repository-layout.md) for the dependency direction and split rationale.

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
