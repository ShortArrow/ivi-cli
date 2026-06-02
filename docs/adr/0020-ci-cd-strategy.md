# 0020. CI/CD Strategy

- Status: Accepted
- Date: 2026-05-22

## Context

Several earlier ADRs delegated specific enforcement to CI:

- ADR 0009 — Integration tests gated to nightly / manual triggers; unit + architecture tests gated on every PR.
- ADR 0017 — `dotnet restore --locked-mode` and Dependabot grouped updates run from CI.
- ADR 0022 — PR title compliance with Conventional Commits enforced server-side, not by local hooks.
- ADR 0024 — bilingual PRD / README sync enforced at PR time (mechanism left to this ADR).
- ADR 0025 — `dotnet format analyzers` runs in CI only (CSharpier handles local formatting).

This ADR fixes the CI platform choice, the workflow inventory, runner matrices, required checks, release artifact strategy, and the versioning scheme.

## Decision

### 1. Platform: GitHub Actions

Use **GitHub Actions** as the sole CI platform. No self-hosted runners are required in Phase 1; GitHub-hosted runners are sufficient for build / test / publish.

### 2. Workflow inventory

Four workflows live in `.github/workflows/`:

| Workflow | Trigger | Purpose |
| --- | --- | --- |
| `pr.yml` | `pull_request` (opened, synchronize, reopened) | Gate for merging into `main` |
| `pr-docker-smoke.yml` | `pull_request` with `paths: docker/**, src/IviCli.Cli/**, .dockerignore` | Paths-filtered mock-VISA container smoke ([ADR 0018](0018-deployment-strategy.md) §9) — runs only on PRs that can break the container image |
| `nightly.yml` | `schedule` (`02:00 UTC`) + `workflow_dispatch` | Integration tests, dependency vulnerability scan, OS matrix |
| `release.yml` | `push` on tag `v*` | Build artifacts, publish GitHub Release, push `dotnet tool` to NuGet, build + push multi-arch mock-VISA container to ghcr.io ([ADR 0018](0018-deployment-strategy.md)) |
| `.github/dependabot.yml` | weekly grouped PRs | NuGet / GitHub Actions / `dotnet-tools.json` updates (config file, not a workflow) |

### 3. `pr.yml` — jobs and gating

Runner: `ubuntu-latest`. Concurrency: `group: pr-${{ github.head_ref }}, cancel-in-progress: true`.

The workflow is split into three jobs (each becomes a required status check on `main`):

| Job | Steps |
| --- | --- |
| `pr-title-validation` | `amannn/action-semantic-pull-request` with the project's Conventional Commits type set |
| `docs-sync-check` | If `docs/PRD.md` is touched, verify `docs/PRD.jp.md` is touched in the same PR (and vice versa). README has the equivalent check. Required only when the relevant paths are modified |
| `build-and-test` | Combined gating job. Steps run sequentially: `dotnet tool restore` → `dotnet restore --locked-mode` → `dotnet csharpier check .` → `dotnet format analyzers --verify-no-changes --no-restore` → `dotnet build --no-restore --configuration Release` → `dotnet test --no-build --configuration Release --filter "Category!=Integration" --collect:"XPlat Code Coverage"` → conditional Codecov upload |

The original ADR draft enumerated six separate jobs (`format-check` / `restore-locked` / `analyzers-check` / `build` / `test` / `coverage-upload`). The implementation collapses them into a single `build-and-test` job because (a) every step depends on the same restored NuGet cache and (b) GitHub's per-step UI surfaces failure attribution at the same granularity as per-job. Coverage upload is conditional on `CODECOV_TOKEN` and uses `fail_ci_if_error: false` — it is therefore non-blocking even within the combined job (matches §6).

All jobs are required for merge; `docs-sync-check` is conditional on path touch.

### 4. `nightly.yml`

Runner matrix: `ubuntu-latest`, `windows-latest`, `macos-latest`. Each OS runs the same job set.

| Job | Notes |
| --- | --- |
| `integration` (matrix per OS) | `dotnet test --filter "Category=Integration"` with TRX artifact upload. NI-VISA hardware tests run only when a self-hosted runner with hardware is configured; otherwise the job degrades to "skip with notice" (`continue-on-error: true`) |
| `dependency-scan` | `dotnet list package --vulnerable --include-transitive`; fails the workflow when high-severity findings appear. The same job runs `dotnet list package --deprecated` as a non-fatal final step (the original ADR draft listed this as a separate `dependency-deprecated` job — collapsing avoids a duplicate restore) |

The nightly workflow does not gate merges; failures open auto-filed issues (deferred — initial revision simply notifies via the workflow run status).

### 5. `release.yml`

Trigger: tag push matching `v[0-9]+.[0-9]+.[0-9]+*`. The release flow per ADR 0022 produces:

| Output | Mechanism |
| --- | --- |
| Self-contained single-file binaries | `dotnet publish -c Release -r <rid> --self-contained -p:PublishSingleFile=true` for `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64` |
| Framework-dependent binaries | `dotnet publish -c Release --self-contained=false` (one per OS) |
| `dotnet tool` package | `dotnet pack` of `IviCli.Cli` configured as `PackAsTool=true`, pushed to nuget.org |
| GitHub Release | `gh release create` with all binaries attached and auto-generated notes from CHANGELOG (manual edits welcome) |
| Source archive | GitHub-provided default tarballs / zips |

Code signing is **not** introduced in Phase 1. Phase 2 adds it (separate ADR).

### 6. Coverage

- Tool: `coverlet.collector` already present in test projects (ADR 0009 §7).
- Upload destination: **Codecov**.
- Policy: visualization only; no minimum coverage threshold gates merges (ADR 0009 §9). Codecov's PR comment serves as the visibility surface.
- Codecov upload token stored as a repository secret; missing token degrades the upload job to a no-op without failing the workflow.

### 7. Required status checks for `main`

Branch protection on `main` requires:

- `pr.yml / pr-title-validation`
- `pr.yml / build-and-test` (covers format / analyzers / restore-locked / build / unit+arch test / coverage upload)
- `pr.yml / docs-sync-check` (conditional on touching `docs/PRD.md` or `docs/PRD.jp.md` or the README pair)

Additional protection settings:

- Require **at least one approving review** (admin override permitted only while the project is effectively single-contributor; revisited when contributors join).
- Disallow force-pushes to `main`.
- Disallow deletion of `main`.
- Require linear history (squash merge already enforces this; the setting is belt-and-suspenders).

### 8. Versioning: Nerdbank.GitVersioning (NBGV)

- `version.json` at the repository root declares the next release's major.minor (e.g. `0.1`).
- Patch is auto-computed from the git commit height since the `version.json` was last bumped.
- Tags `v0.1.0`, `v0.1.1`, ... drive the release workflow. NBGV embeds the version into assemblies and into the produced NuGet package metadata.
- Major / minor bumps happen via PR to `version.json`. The bump PR is the formal "start of a new release window".

### 9. Caching

- `actions/setup-dotnet@<sha>` enables built-in NuGet cache via `cache: true`.
- Tool cache: `~/.dotnet/tools` (`actions/cache`).
- `bin/` and `obj/` are **not** cached (build determinism > cache hit).

### 10. Third-party action pinning

All third-party actions are pinned by commit SHA (e.g. `actions/checkout@8a4c...`), not by `v4` tag.

- Dependabot is configured to track action SHA updates (`package-ecosystem: github-actions`).
- Rationale: blunt mitigation against supply-chain attacks on action repositories (consistent with ADR 0017 §5).

### 11. Concurrency

- PR workflow uses `concurrency: { group: pr-${{ github.head_ref }}, cancel-in-progress: true }`. New pushes to a feature branch cancel in-progress jobs for that branch.
- Release workflow uses `concurrency: { group: release, cancel-in-progress: false }` to prevent overlapping releases without losing any in-flight run.

### 12. Secrets

| Secret | Purpose | Phase |
| --- | --- | --- |
| `CODECOV_TOKEN` | Coverage upload (optional for public repos) | 1 |
| Code signing certificate | Binary signing | 2 |

All secrets stored as repository / environment secrets; never written to logs.

**nuget.org auth is keyless.** `dotnet tool` publish uses [NuGet Trusted
Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
(OIDC) via `NuGet/login@v1` rather than a long-lived `NUGET_API_KEY`
secret. The `pack` job in `release.yml` declares
`permissions: { id-token: write }` and a matching policy is registered
under the `ShortArrow` nuget.org account
(`Repository Owner = ShortArrow`, `Repository = ivi-cli`,
`Workflow = release.yml`).

**ghcr.io auth is also keyless.** The `docker` job uses the
built-in `GITHUB_TOKEN` with `packages: write` — no PAT required.

### 13. Analyzer enforcement

- `dotnet format analyzers --verify-no-changes` runs as a required PR check.
- Analyzer rule severity is governed by the repository's `.editorconfig`. The default starts at the SDK's analyzer baseline; per-rule adjustments are made in `.editorconfig` rather than via `<NoWarn>` in csproj.
- New rule categories that produce noise on existing code are either fixed or explicitly demoted in `.editorconfig` with a PR-described rationale.

### 14. Workflow file format

- All workflows authored in YAML, schema-validated by `actionlint` in pre-commit (deferred — added when CI maturity grows).
- Each job's `runs-on`, `permissions`, and `timeout-minutes` are explicit. Default `permissions` is `contents: read`; jobs that publish (release) declare `contents: write`, `packages: write` as required.

## Consequences

**Pros**

- Required checks make the contract for `main` mergeability auditable and uniform.
- PR-time gating stays under a few minutes (single-OS, unit + architecture only); nightly carries the slower work.
- Three artifact forms (self-contained, framework-dependent, `dotnet tool`) cover automation engineers, .NET developers, and container users without compromise.
- NBGV + tag-driven release decouples release engineering from manual `<Version>` edits.
- SHA-pinned actions + locked NuGet restore + Dependabot SHA tracking close the most common supply-chain vectors at minimal cost.

**Cons**

- Four workflows + Dependabot config to maintain; small but real surface.
- Cross-OS coverage is only nightly, so an OS-specific regression introduced by a PR is detected the day after rather than at PR time.
- `dotnet format analyzers` is slower than CSharpier and runs only in CI; contributors discover analyzer-rule violations only after pushing.

**Mitigations**

- Workflows are short, declarative YAML; changes are localized.
- The OS-specific gap is tolerable for a CLI whose Phase 1 surface is mostly cross-platform .NET code. A PR may opt in to a full-OS run via `workflow_dispatch` when an OS-sensitive change is suspected.
- Document the local `dotnet format analyzers` command in CONTRIBUTING so contributors can pre-check before pushing.
