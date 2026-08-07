# 0047. Quality Assurance and Support Scope

- Status: Accepted
- Date: 2026-07-24

## Context

ivi-cli is pre-1.0 and ships through four channels — self-contained
binaries for six RIDs, a `dotnet tool` package, the mock-VISA
container, and source ([ADR 0018](0018-deployment-strategy.md)). Its
primary external consumers are **3rd-party e2e pipelines that embed
the mock container in their CI** and lab operators driving real
instruments. Both need to know what they can depend on: which
artifacts were actually executed before release, which interfaces are
safe to script against, and what happens when a real instrument
misbehaves. Until now that boundary existed only implicitly — in the
CI workflows, in scattered ADRs, and in the licenses' warranty
disclaimers, none of which tell a consumer what the maintainers *aim*
to uphold.

This ADR fixes the quality-assurance and support responsibility
scope in one place. The licenses (MIT OR Apache-2.0) continue to
disclaim all legal warranty; everything below is a maintenance
commitment, not a guarantee.

## Decision

### 1. What every release verifies

A `vX.Y.Z` release is blocked unless all of the following pass, in a
clean CI environment, at the tagged commit:

| Artifact | Verification |
| --- | --- |
| Self-contained binary, each of 6 RIDs | Build + unit/architecture suite + start-up smoke of the published binary, on a **native runner of the same OS/arch** ([ADR 0016](0016-cross-platform-policy.md)) |
| Mock container (`linux/amd64` + `linux/arm64`) | HEALTHCHECK + SCPI round-trip smoke, each architecture natively, before the multi-arch push |
| `dotnet tool` nupkg | Packed from the same commit; carries the same IL as the smoke-run binaries, not separately executed |
| Version metadata | Tag = `Directory.Build.props` `<Version>` = CHANGELOG entry (release guard job) |

Known, accepted gap: the Integration suite (real sockets, PyVISA
interop) runs on `nightly.yml` across three OSes but is **not** a
release gate; a release may ship between a regression and the nightly
that catches it.

### 2. Compatibility contract (pre-1.0)

While the version is 0.x, two surfaces are treated as **stable
contracts**:

1. **`--json` output schemas** of every command.
2. **`config.toml` schema** (devices, servers, routes, and the
   scenario TOML format).

A breaking change to either requires a **minor** version bump
(0.Y → 0.Y+1) and an explicit CHANGELOG entry naming the break;
patch releases never break them. Both surfaces are pinned in CI by
Verify snapshots and parser tests, so an accidental break fails the
PR, not the consumer.

Everything else — human-readable output, log text and levels, the
Management API shape, exit-message wording, container internals
(base image, layout), and all .NET APIs of the shipped assemblies —
may change in any release until 1.0. From 1.0 on, Semantic
Versioning applies to the whole public surface.

### 3. Real-instrument compatibility: point-in-time verified list

Compatibility with physical instruments is handled by a
**verified-instrument list** (published in
[docs/verified-instruments.md](../verified-instruments.md)):

- An entry records the instrument model, the protocols exercised,
  the ivi-cli version at which the verification happened, and any
  device-side caveats found.
- Entries are **point-in-time**: they state what was observed at the
  recorded version and are *not* re-verified on subsequent releases.
  Continuous re-verification would grow an unbounded hardware test
  matrix; protocol behaviour is instead protected by the automated
  suites (gateway round-trips, PyVISA interop, RPC/framing unit
  tests), which do run on every change.
- Instruments not on the list fall under **best-effort standards
  conformance**: ivi-cli targets the published HiSLIP, VXI-11,
  raw-SOCKET, and IEEE 488.2 / SCPI specifications, and instrument
  incompatibilities are treated as bugs when the instrument follows
  those specifications. Reports with an `IVICLI_CAPTURE` traffic log
  attached are the expected input.

### 4. Support level

- Support happens through **GitHub issues, best-effort, no SLA**.
- Only the **latest release** is supported. Fixes ship as a new
  release from `main`; there are no backport branches
  ([ADR 0022](0022-branching-strategy.md) — trunk only).
- Vulnerability reports go through the private channel defined in
  `SECURITY.md`, not public issues.

## Consequences

- Consumers can pin CI on the two contract surfaces (`--json`,
  `config.toml`) with a documented upgrade signal (minor bump +
  CHANGELOG), instead of guessing which parts of a 0.x tool are safe
  to script.
- The verified-instrument list gives buyers of bench time an honest
  datum without committing the project to a hardware regression
  matrix it cannot sustain.
- "Latest release only" keeps the maintenance surface at one branch;
  users who cannot upgrade must vendor a fix themselves.
- The README's user-facing summary of this scope must be kept in
  sync with this ADR when either changes.
