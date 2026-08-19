**English** | [日本語](CONTRIBUTING.jp.md)

# Contributing to ivi-cli

Thanks for considering a contribution. This file is a **navigation hub** — each topic links to the Architecture Decision Record (ADR) that governs it. Policies live in those ADRs so they have a single source of truth.

## Getting started

```sh
git clone https://github.com/ShortArrow/ivi-cli.git
cd ivi-cli
dotnet tool restore
dotnet restore --locked-mode
dotnet build
dotnet test --filter "Category!=Integration"
```

The first `dotnet build` runs `dotnet husky install` and wires the local hooks.

## Branching & commits

GitHub Flow (single `main`, short-lived topic branches, squash-merge, Conventional Commits) — see [ADR 0022](adr/0022-branching-strategy.md).

## Local hooks

Husky.Net pre-commit runs CSharpier. Build and tests are not run on push — that is CI's job (`pr.yml`) — see [ADR 0025](adr/0025-dev-automation-hooks.md).

## CI gating

`pr.yml` (PR), `nightly.yml` (scheduled, includes Integration), `release.yml` (tagged) — see [ADR 0020](adr/0020-ci-cd-strategy.md). The `docs-sync-check` job enforces that bilingual doc pairs stay in lock-step.

## Adding a dependency

A new package reference is also a redistribution obligation: its assembly ships beside `ivicli` in the release archives, the container image, and the tool package. Add an entry for it to `THIRD-PARTY-NOTICES.md` in the same PR — the `notices-sync` job compares that file against `src/IviCli.Cli/packages.lock.json` and fails on a package with no entry as well as on an entry with no package. Read the terms from the project itself rather than from the package: several dependencies declare no license at all in their NuGet metadata. See [ADR 0046](adr/0046-licensing.md).

## Documentation

All repository documentation is English-primary. Japanese mirrors (`*.jp.md`) are required for the PRD, README, and this file via an `**English** | [日本語](...)` switcher header — see [ADR 0024](adr/0024-documentation-policy.md).

## Architecture

Clean Architecture + DDD + handler-level CQRS (see [ADR 0003](adr/0003-architecture-style.md)) realised across the layer assemblies under `src/` and `tests/` (see [ADR 0021](adr/0021-repository-layout.md)); DI composition is governed by [ADR 0010](adr/0010-dependency-injection.md). The `NetArchTest` suite under `tests/IviCli.Cli.Tests/Architecture/` enforces the dependency direction on every PR.

## Tests

xUnit + Shouldly + Testably.Abstractions. Tests mirror `src/` 1:1 (`IviCli.<Layer>.Tests`). TDD (Red → Green → Refactor) is expected for behavioural changes.

Integration tests carry `[Trait("Category", "Integration")]`; they are skipped by default in the PR build, and run on `nightly.yml`.

## Quality assurance & support scope

What each release verifies, the pre-1.0 compatibility contract, the verified-instrument policy, and the support level — see [ADR 0047](adr/0047-quality-assurance-and-support-scope.md). Platform coverage (native test + smoke per shipped RID) — see [ADR 0016](adr/0016-cross-platform-policy.md). Vulnerabilities go through [SECURITY.md](../SECURITY.md), not public issues.

## ADRs

Every accepted decision lives under [`docs/adr/`](adr/). They are living documents — extend them in place via normal PRs; supersede only on genuine reversal. No index file is maintained.
