# 0025. Local Development Automation Hooks

- Status: Accepted
- Date: 2026-05-21

## Context

This ADR defines the **local-side** automation hooks that fire during development: git client-side hooks and Claude Code hooks. Continuous Integration on the server side is the subject of a separate ADR (0020).

Two distinct concerns drive this:

1. **Provide fast feedback** for trivial breakage (formatting drift, build failures, broken tests) before the developer pushes or opens a PR — minimizing wasted CI cycles and avoiding round-trips through GitHub Actions.
2. **Keep AI-driven edits clean.** Claude Code performs file edits as part of its normal workflow, and without an automatic formatter step those edits can drift from the project's style rules.

The real gates that decide whether a change reaches `main` are CI checks and squash merge (ADR 0020, 0022). Local hooks are not the last line of defense — they are a UX layer for catching obvious mistakes early.

## Decision

### 1. Git hook runner: Husky.Net

Use **Husky.Net** as the git hook runner.

- Installed via `dotnet tool` (project-local), restored as part of `dotnet tool restore`.
- The `.husky/` directory is committed and shared.
- Rationale: Husky.Net fits naturally in a .NET-only project and avoids Node.js or Python dependencies.

Initial setup (documented in CONTRIBUTING when it exists):

```bash
dotnet tool restore
dotnet husky install
```

### 2. Formatter choice: CSharpier (local) + `dotnet format analyzers` (CI)

The formatting toolchain is split by responsibility:

- **Whitespace, line breaks, and code layout** — handled by **CSharpier** (NuGet local tool, `csharpier`). Opinionated, Prettier-style, no project-specific tuning.
- **Analyzer rule auto-fix** (IDExxxx, naming rules, etc.) — handled by **`dotnet format analyzers`** in CI only (see ADR 0020).

Rationale:

- CSharpier is one to two orders of magnitude faster than `dotnet format` in default execution, which keeps pre-commit and `PostToolUse` hooks sub-second.
- CSharpier's opinionated approach eliminates style debates and reduces churn from `.editorconfig` drift.
- Analyzer-fix logic is heavier and benefits more from being run once on full PRs in CI than on every local commit.

### 3. pre-commit: CSharpier format verification only

Run **`csharpier --check .`** on commit. If formatting drift is detected, the commit is **rejected** (not auto-fixed), and the developer is asked to run `csharpier .` and re-stage.

Rationale:

- Auto-apply on commit can quietly modify files beyond the developer's intent and is harder to review.
- CSharpier's output is deterministic across machines, so violations are unambiguous.
- pre-commit must stay fast (sub-second). Format check qualifies; tests do not.

### 4. pre-push: build + unit/architecture tests

Run **`dotnet build`** followed by **`dotnet test --filter "Category!=Integration"`** on push. If either fails, the push is rejected.

Rationale:

- Squash merge means individual commit greenness does not survive to `main`, so blocking every commit with tests fights TDD's Red phase commits unnecessarily.
- Pre-push is the latest local moment to catch broken code before it touches the remote.
- Integration tests are excluded (per ADR 0009) — they belong in nightly / manual CI runs.

### 5. Claude Code PostToolUse hook: auto-format edited C# files

Configure a Claude Code `PostToolUse` hook in **`.claude/settings.json`** (shared, committed) that runs `csharpier <path>` after `Edit` or `Write` modifies a `.cs` file.

- The hook is **non-blocking** — formatting failures emit a warning but do not reject the edit.
- This keeps AI-driven changes consistent with project style without interrupting the editing flow.
- `.claude/settings.local.json` remains for per-developer overrides and is gitignored.

### 6. Commit message validation: skipped locally

No local `commit-msg` hook is configured.

- Squash merge means the PR title becomes the merged commit's first line.
- Conventional Commits compliance is enforced at the **PR title level** by GitHub Actions (specified in ADR 0020).
- Individual feature-branch commit messages may be free-form (e.g. `wip`, `fixup`, `red`); they are squashed away at merge.

### 7. Bypass mechanism

`git commit --no-verify` and `git push --no-verify` are **not blocked** at the tooling level. Their use is reserved for emergencies (e.g. recovering from a broken local state).

- When used, the developer must note the bypass in the eventual PR description.
- CONTRIBUTING (when written) will reiterate this norm.
- The CI checks on the server side enforce the same rules regardless of local bypass.

### 8. File-type filtering: deferred

A `staged-file-type` filter (skipping format/test when only docs are changed, etc.) is **not** introduced in Phase 1. Test runs are expected to be sub-second for the foreseeable future, so the simplicity of unconditional execution is preferred.

If pre-push run time exceeds a few seconds on a routine basis, this decision is revisited.

### 9. Branch-aware policy: not adopted

Differing hook behavior by branch (`main` vs feature) is **not** adopted.

- Direct commits to `main` are forbidden by ADR 0022, so branch-specific local hooks would be redundant with the server-side protection.
- Server-side CI is the authoritative gate.

### 10. Concrete hook scripts (sketch)

`.husky/pre-commit`:

```sh
#!/bin/sh
. "$(dirname "$0")/_/husky.sh"
dotnet csharpier --check .
```

`.husky/pre-push`:

```sh
#!/bin/sh
. "$(dirname "$0")/_/husky.sh"
dotnet build --nologo --verbosity minimal
dotnet test --no-build --filter "Category!=Integration" --nologo --verbosity minimal
```

`.claude/settings.json` (PostToolUse hook for CSharpier):

```jsonc
{
  "hooks": {
    "PostToolUse": [
      {
        "matcher": "Edit|Write",
        "filter": { "path": "**/*.cs" },
        "command": "dotnet csharpier {{path}}",
        "blocking": false
      }
    ]
  }
}
```

Exact syntax for the Claude Code hook is verified against the current Claude Code schema at implementation time; the principle above is binding.

## Consequences

**Pros**

- Trivial drift (formatting, build breakage, unit-test failure) caught locally; CI cycles are reserved for genuine integration concerns.
- AI-driven edits stay aligned with project style automatically.
- Pre-commit and Claude Code hooks remain sub-second thanks to CSharpier's speed.
- Setup is single-language (.NET only); no Node.js, Python, or Go in the contributor toolchain.
- Local hooks remain simple; no branch-aware or file-type-aware logic to maintain in Phase 1.

**Cons**

- First-time clone requires `dotnet tool restore` + `dotnet husky install` to enable hooks (documented in CONTRIBUTING when written).
- Two formatting tools to understand (CSharpier locally vs `dotnet format analyzers` in CI), though their responsibilities are disjoint.
- `--no-verify` bypass exists; relies on developer discipline rather than enforcement.
- Pre-push test run may grow slow as the test suite expands; revisiting the filter strategy will eventually be needed.

**Mitigations**

- README will mention the one-time setup once it exists.
- CONTRIBUTING (when written) will clarify which tool covers which class of changes.
- CI checks (ADR 0020) enforce the same rules unconditionally on the server side; local bypass cannot reach `main`.
- Add file-type filtering or scope reduction (e.g. `dotnet test --filter` by affected project) when run time becomes a complaint.
