# 0022. Branching Strategy

- Status: Accepted
- Date: 2026-05-19

## Context

This ADR fixes the branching policy. IVI-CLI is a CLI tool OSS; it has no parallel-support releases and no long-lived maintenance branches.
On the premise of continuous delivery, single product, and single trunk, it follows the de facto standard among modern CLI OSS (`gh`, `kubectl`, `helm`, `terraform`, `ripgrep`, `bat`, `uv` / `ruff`, and nearly all others).

## Decision

### Branch model: GitHub Flow

- `main` is the only long-lived branch. It is kept in a releasable state at all times.
- feature/fix/docs/... branches are cut from `main` and return to `main` via PR as short-lived branches.
- No `develop` branch is maintained.
- Releases are made by placing an annotated tag (e.g. `v0.1.0`) on a commit of `main`.
- The one exception is a maintenance branch, below. Long-term support is not offered.

### Maintenance branch: a release owed on an older line

A `release/X.Y.x` branch is cut from an existing tag when a release on an
older minor line is owed and `main` can no longer produce it, because
`main` already carries the next line's breaking changes. The case that
introduced it: a deprecation warning promised for 0.3.x had to ship before
0.4.0 removed the old form, and `main` was already 0.4.0.

- It is cut from the latest tag on that line, pre-release or not, so the
  release inherits what that line already published.
- It takes only changes that break nothing on that line. Each lands through
  a PR into the branch, squash-merged; `pr.yml` runs for PRs into
  `release/**` as it does for `main`.
- A fix that also applies to `main` lands on `main` first and is ported,
  so `main` never lacks a fix that an older line has.
- Its release section is also recorded in `main`'s CHANGELOG, so the one
  file keeps every release.
- A tag on it runs the same release workflow. While its line is still the
  newest stable, the release moves the floating `latest` / `vX.Y` tags as
  any stable does. Once a newer line has a stable release, a maintenance
  release must not take `latest` or the GitHub "Latest release" mark
  back; that needs a change to `release.yml` before the first such tag.
- The branch ends with the release it was cut for, and is deleted after.

### Branch naming

`<type>/<short-kebab-slug>` format. `type` matches the type in Conventional Commits.

```
feat/visa-scan
fix/session-state-race
docs/adr-0022-branching
chore/editorconfig
refactor/backend-interface
test/fake-backend-disconnect
perf/scan-parallelism
build/directory-build-props
ci/github-actions
hotfix/crash-on-empty-config
```

- ADR / Issue numbers are optional. For now, embed them only for ADRs, as in `docs/adr-NNNN-...`.
- This convention is not enforced for PRs from contributor forks (OSS convention).

### Direct push to main: forbidden

All changes go through a PR. No exceptions.
The sole historical exception is the foundational commits landed before this ADR (`0148705`, `cd31c05`, `a840b9d`, `415892f`, `537b051`); these remain as direct commits on `main` and are not migrated.

### Merge strategy: squash merge

- PR → `main`: **Squash and merge** is the default. The history of `main` is linear, with 1 PR = 1 commit.
- The post-squash commit message matches the PR title and follows the Conventional Commits convention.
- As an exception, rebase merge is selected only when multiple logically independent commits need to be preserved (requires prior agreement).
- Merge commits (`--no-ff`) are not used.

### Commit message: Conventional Commits

```
<type>(<optional-scope>): <subject>

[optional body]

[optional footer(s)]
```

- The `type` set is identical to the branch prefixes.
- `scope` is the target assembly name or subsystem name (e.g. `feat(backends-hislip): ...`, `docs(adr): ...`).
- For a breaking change, include `BREAKING CHANGE:` in the footer or use the `type!:` notation.
- The existing commit history (`docs:`, `chore:`, `docs(adr):`) is consistent with this convention.

### Release flow

1. Confirm `main` is in a releasable state (CI green / smoke tested).
2. Place an annotated tag on `main`, or on a maintenance branch for a release owed on an older line: `git tag -a v0.1.0 -m "..."`.
3. Pushing the tag triggers GitHub Actions to produce the release / artifacts (CI setup is covered separately in 0020).
4. When a hotfix is needed, cut a `hotfix/*` branch from `main` and return it through a normal PR.

## Consequences

**Pros**

- Low barrier for contributors (a single PR target).
- Simple CI/CD pipeline (triggered by PRs into `main` or a maintenance branch, and by tag push).
- Linear, readable history on `main`.
- Matches the expectations of readers familiar with modern CLI OSS.

**Cons**

- A maintenance branch lives outside `main` for as long as its release takes, and a fix it needs has to be ported by hand.
- Squash merge means intermediate commits inside a PR are lost from the `main` history (they remain on the PR).

**Mitigations**

- A maintenance branch is cut only for a release already owed and ends with it, so it never becomes a second trunk.
- Important intermediate decisions are recorded in the ADR / PR description, limiting the impact of squash loss.
