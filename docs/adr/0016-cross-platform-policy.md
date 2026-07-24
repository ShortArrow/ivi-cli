# 0016. Cross-Platform Policy

- Status: Accepted
- Date: 2026-07-24

## Context

ivi-cli ships self-contained and framework-dependent binaries for six
RIDs (`linux-x64`, `linux-arm64`, `win-x64`, `win-arm64`, `osx-x64`,
`osx-arm64`) plus a multi-arch (`linux/amd64` + `linux/arm64`) mock
container ([ADR 0018](0018-deployment-strategy.md)). Until this ADR,
only three of those architectures were ever *executed* before a
release: the publish matrix ran on x64 (and Apple-silicon macOS)
runners, cross-publishing `linux-arm64`, `win-arm64`, and `osx-x64`
without running the result. A publish-side fault visible only at
runtime — a bad RID mapping, a missing native library in the
single-file bundle — would have surfaced on a user's machine, not in
CI. GitHub-hosted native arm64 runners (`ubuntu-24.04-arm`,
`windows-11-arm`) and the Intel macOS runner (`macos-15-intel`) are
now available to public repositories at no cost, which removes the
original reason for cross-publishing.

## Decision

### 1. Supported platform set

The supported OS/architecture set **is** the six shipped RIDs. No RID
is shipped without the verification in §2; conversely, adding a RID to
the release matrix implies adding its native verification.

### 2. Release gate: native execution per RID

`release.yml`'s publish matrix pairs every RID with a runner of the
**same OS and architecture**:

| RID | Runner |
| --- | --- |
| `linux-x64` | `ubuntu-latest` |
| `linux-arm64` | `ubuntu-24.04-arm` |
| `win-x64` | `windows-latest` |
| `win-arm64` | `windows-11-arm` |
| `osx-x64` | `macos-15-intel` |
| `osx-arm64` | `macos-latest` |

Each matrix job builds, runs the unit + architecture test suite, and
**smoke-runs the published self-contained binary** (`ivicli --version`
must succeed and report the tag-derived version). A binary that cannot
start on its target architecture fails the release before anything is
published.

The framework-dependent binary is published per-RID but not separately
smoke-run: it carries the same IL as the self-contained bundle and its
runtime is supplied by the user.

### 3. Container: both manifest halves executed

The mock container's smoke gate runs **natively on both
architectures** before the multi-arch push: amd64 in the `docker` job,
arm64 in `docker-smoke-arm64` on an arm64 runner (shared script
`docker/smoke-test.sh` — HEALTHCHECK + SCPI round-trip). QEMU
emulation is deliberately not used for the smoke: emulated .NET is
slow and flaky, and a native runner exists.

### 4. PR and nightly scope

Cross-platform spread is a **release gate, not a PR gate**. PR CI
(`pr.yml`) stays on `ubuntu-latest` only, keeping the feedback loop
fast; `nightly.yml` runs the Integration suite on `ubuntu-latest`,
`windows-latest`, and `macos-latest`. Rationale: the unit suite is
platform-neutral .NET, and the architecture-specific failure modes
observed in practice live in publish/packaging — exactly what §2
covers on every release.

### 5. Platform-specific functionality

The Local backend's `Ivi.Visa` reflection path is **Windows-only**
(the IVI Shared Components have no Linux/macOS distribution). On other
platforms it degrades to a clean `BackendError`, and its
positive-side tests gate on the `ni-visa` prerequisite probe
([ADR 0037](0037-real-hardware-integration-tests.md)). All other
features — gateways, mock/replay, discovery, Management API — are
platform-neutral by construction.

## Consequences

- Every shipped binary and both container manifest halves have been
  executed at least once before users can download them; the
  "cross-published but never run" class of release defect is closed.
- The release pipeline uses three additional runner images
  (`ubuntu-24.04-arm`, `windows-11-arm`, `macos-15-intel`). All are
  free for public repositories, but runner-label retirement (GitHub
  rotates macOS/Windows images) will occasionally force a label bump
  here and in `release.yml`.
- Release wall-clock changes little: the matrix was already six jobs;
  they now differ in host arch rather than duplicating hosts.
- PR feedback speed is unchanged — no PR job was added.
