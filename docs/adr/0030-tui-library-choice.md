# 0030. TUI Library Choice

- Status: Accepted
- Date: 2026-05-27

## Context

`ivicli visa watch` (PRD §15 "Planned: watch") needs to render a
periodically refreshed table of per-device status to the operator's
terminal. Manual VT100 sequences would be ad-hoc; rolling our own
table renderer would mostly reinvent existing libraries and create
maintenance overhead the project does not need. A third-party TUI
library is the right move.

The wider Cli surface (`visa list`, `visa status`, `server list`, etc.)
prints plain strings today; the new dependency must not push existing
verbs to adopt heavyweight rendering they do not want.

## Decision

Adopt **[Spectre.Console](https://spectreconsole.net/)** for the `visa
watch` live table and any future verbs that need rich terminal output.
Pinned at `0.50.0` in `Directory.Packages.props`; updated on regular
dependency-bump cadence.

### Why Spectre.Console

- **.NET-native, MIT-licensed, actively maintained.** No FFI, no
  cross-language tooling, low-friction CI.
- **`LiveDisplay` API** maps directly onto the `IWatchDevicesSink`
  abstraction: one call per tick refreshes the table in place.
- **No alternative-screen takeover by default.** Output degrades to
  plain text when stdout is redirected, which matches the existing
  `--json` / `--plain` flag pattern.
- **Header-row + per-row colour control** without escape-code
  handcrafting.

### Alternatives considered (and not chosen)

- **Terminal.Gui** — fuller TUI toolkit (windows, menus, focus).
  Out of proportion for a single live table; pushes us toward a
  multi-screen UX we do not want yet.
- **Textual / urwid** — Python-only; the project is .NET 10.
- **Hand-rolled VT100 escape sequences** — explicitly ad-hoc; trades
  one dependency for fragile boilerplate.

### Scope of use

- v1: only the `visa watch` verb depends on Spectre.Console.
- `--json` and `--plain` flags on `visa watch` continue to bypass the
  library entirely so headless / CI usage stays ANSI-free.
- Other verbs may adopt it in future ADRs (e.g. a `visa scan`
  formatted output), but no global migration is planned. Each adoption
  requires a one-line justification in the relevant PR.

### Layer placement

Spectre.Console is referenced only from `IviCli.Cli`. The Application
layer continues to define presentation-agnostic ports
(`IWatchDevicesSink`, etc.); the CLI provides the Spectre-backed
adapter. The dependency-direction tests
(`tests/IviCli.Cli.Tests/Architecture/DependencyDirectionTests.cs`)
keep the rule: backends / server / infrastructure must not depend on
Spectre.Console.

## Consequences

- One new third-party package + transitive closure in the Cli output
  binary (≈ 1 MB increase).
- Future TUI features have a known target API; replacing Spectre.Console
  later is a one-ADR rewrite (the abstraction sits in
  `IWatchDevicesSink`, not in the Application layer).
- CI / Husky pre-push behaviour unchanged — Spectre.Console renders to
  a captured stream when not on a TTY.
