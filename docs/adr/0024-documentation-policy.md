# 0024. Documentation Policy

- Status: Accepted
- Date: 2026-05-21

## Context

IVI-CLI is a CLI tool that operates on VISA / IVI / HiSLIP / VXI-11 — all international standards — and is positioned as OSS. Its primary contributors today are Japanese-native, but the audience and potential contributor base are international.

We need a single source of truth for:

- Which kinds of documents the project keeps, where they live, and who they target.
- The natural language of those documents (English-primary vs bilingual).
- ADR conventions (numbering, required sections, lifecycle, update policy).
- Inline code documentation scope (XML doc comments).
- CHANGELOG strategy.
- The pattern for paired bilingual documents (PRD-style language switcher).

This ADR establishes those conventions. It is the first ADR written in English; pre-existing Japanese ADRs and the domain glossary will be translated in a follow-up commit per this policy.

## Decision

### 1. Document catalog

| Category | Location | Purpose |
| --- | --- | --- |
| PRD | `docs/PRD.md` (+ optional `docs/PRD.jp.md`) | Product requirements; reader-facing |
| ADR | `docs/adr/NNNN-*.md` | Architectural decisions; living documents tracked in git |
| Domain Glossary | `docs/domain-glossary.md` | Ubiquitous-language catalog of Entities / Value Objects / Domain Services |
| README | `/README.md` (+ optional `/README.jp.md`) | Project intro, install, quick start; reader-facing |
| CONTRIBUTING | `/CONTRIBUTING.md` | Developer onboarding |
| CHANGELOG | `/CHANGELOG.md` | Release-by-release user-visible changes |
| User manual / tutorials | `docs/user/` (future) | Long-form user-facing docs |
| API reference | Generated from XML doc comments (future) | Public API surface |
| `--help` text | Embedded in code via System.CommandLine annotations | Authoritative source for CLI usage |

README, CONTRIBUTING, and CHANGELOG are deferred until they have a non-trivial first revision (README when Phase 1 scaffolding is runnable; CONTRIBUTING when an external contributor is anticipated; CHANGELOG at the `v0.1.0` release tag).

### 2. Language policy

- **English is the primary language for all repository artifacts** — ADRs, domain glossary, code identifiers, code comments, docstrings, commit messages, PR titles/descriptions, `--help` text, CHANGELOG, CONTRIBUTING.
- **Japanese translations are permitted only for reader-facing documents** as i18n companions: PRD and README. ADR / glossary / source artifacts are English-only.
- Paired bilingual documents use the language-switcher pattern (see §4).
- Translations must be kept in sync within the same PR that changes the canonical English. If sync is broken and not repairable, the lagging translation file is archived rather than left misleading.

### 3. ADR conventions

- **Filename**: `NNNN-kebab-slug.md`. `NNNN` is a 4-digit sequential number. Numbers are never reused, even for deleted ADRs.
- **Required header fields**: `Status`, `Date`.
- **Required sections**: `Context`, `Decision`, `Consequences`. Additional sections are allowed below these.
- **Status values**:
  - `Draft` — under discussion, not yet binding.
  - `Proposed` — finalized text awaiting acceptance.
  - `Accepted` — binding; the project follows this decision.
  - `Deprecated` — no longer recommended, but no replacement is mandated.
  - `Superseded by NNNN` — replaced by a newer ADR.
- **Living documents**: ADRs are maintained like the PRD — they may be updated in place through normal PRs to refine, clarify, or revise a decision. The `Date` field reflects the original acceptance and is not bumped for ordinary updates; the git history is the authoritative change log.
- **When to create a new ADR instead**: a new ADR (with the older one's `Status` set to `Superseded by NNNN`) is preferred only when (a) the change is large enough that preserving the original decision aids history, (b) the original ADR is widely cross-referenced and rewriting it would confuse readers, or (c) the decision reverses rather than refines. In all other cases, edit in place.
- **No external references**: ADRs must not cite files outside the repository (user-global config, private notes, chat transcripts). If a principle is load-bearing, restate its content inline.
- **Numbering gaps**: it is acceptable to skip numbers (e.g. accepting 0021 before 0010). Skeleton files for planned ADRs may exist with `Status: Draft`.

### 4. Bilingual document switcher

Paired documents place the switcher at the top, with the current language emphasized in bold and the other as a relative link:

```markdown
**English** | [日本語](PRD.jp.md)
```

and on the JP side:

```markdown
[English](PRD.md) | **日本語**
```

Both files share the same heading structure and section order so cross-reference is trivial.

### 5. Domain glossary

`docs/domain-glossary.md` is a living document:

- Updated via normal PRs without requiring a new ADR.
- Classification changes (e.g. promoting a Value Object to an Entity) must be justified in the PR description.
- The glossary is the single source of truth for type names and terminology used in code, tests, logs, and other documents.

### 6. Code documentation (XML doc comments)

- **Scope**: XML doc comments are **required on the public API surface** — public types, public methods, public properties, and public records exposed by each assembly's public namespace.
- **Internal and private** members get comments only when the *why* is non-obvious. Names should carry the *what*.
- **`<summary>`** is required on public members. **`<param>`, `<returns>`, `<exception>`** are required when relevant (side effects, failure modes, exception contracts).
- **`<remarks>`** is the place to document contract evolution and forward compatibility notes.
- Inline `//` comments inside method bodies are reserved for non-obvious *why* (hidden constraints, subtle invariants, workarounds). Do not narrate *what* the code does.

### 7. CHANGELOG

- Format: [Keep a Changelog](https://keepachangelog.com/) style, hand-curated.
- Conventional Commits in git history serve as a hint, not an auto-generation source. Maintainer decides what is user-visible and how to phrase it.
- The file is created at the `v0.1.0` release tag, not before.

### 8. README (deferred)

When created, it must contain:

- One-sentence project description.
- Stability indicator (`alpha` / `beta` / `stable`).
- Quick install and quick usage example.
- Links to PRD, the ADR directory, and the domain glossary.
- License.

It does not contain a hand-maintained ADR index (manual index files are explicitly excluded from this project's documentation set due to maintenance burden).

### 9. CONTRIBUTING (deferred)

When created, it must reference:

- Required toolchain versions (.NET, OS support).
- Branching strategy (ADR 0022).
- Testing requirements (ADR 0009).
- Commit message convention (Conventional Commits).
- The fact that the documentation language is English (this ADR).

### 10. Migration plan for pre-existing Japanese documents

In effect as of this ADR's acceptance:

1. The five accepted Japanese ADRs (`0003`, `0009`, `0021`, `0022`, `0023`) will be translated to English in a single follow-up commit. The translation is an in-place update; no supersession is required.
2. `docs/domain-glossary.md` will be translated to English in the same follow-up commit.
3. Skeleton ADRs `0001`–`0020` already have English titles; their bodies remain `TBD` and are unaffected.
4. `docs/PRD.md` (English) is already the canonical PRD; `docs/PRD.jp.md` remains as the optional bilingual companion under §4.

## Consequences

**Pros**

- Documentation is accessible to international contributors and users.
- Single language for the repository reduces sync overhead (only PRD/README have i18n companions).
- ADRs evolve in place under git, and the PR + commit history serves as the authoritative change log; supersession is reserved for genuine reversals or large rewrites.
- Authoritative source of `--help` text being in code prevents drift between code and docs.

**Cons**

- Japanese-native contributors carry a small reading-comprehension cost for ADRs and glossary.
- Bilingual PRD / README require synchronized PRs; partial updates are not allowed.

**Mitigations**

- ADRs are short, structured, and follow a fixed template. Reading-comprehension cost amortizes as contributors become familiar with the recurring vocabulary.
- For paired documents, CI or PR review enforces sync (specific mechanism deferred to the CI ADR 0020).
- AI-assisted reading is a practical fallback for any contributor who needs a quick summary in their native language.
