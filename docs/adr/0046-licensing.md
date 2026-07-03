# 0046. Licensing

- Status: Accepted
- Date: 2026-07-03

## Context

The repository shipped through `v0.2.7` without a license file. The README
declared the source "all rights reserved" as a placeholder, which blocks any
downstream reuse, packaging, or contribution and is incompatible with the
project's OSS positioning (ADR 0024).

ivi-cli is distributed both as a `dotnet tool` NuGet package (`ivi-cli`) and as
self-contained per-RID binaries. It links a stack of permissively licensed
dependencies (Serilog, Spectre.Console, System.CommandLine, OpenTelemetry,
Tomlyn, Makaretu.Dns — all MIT / Apache-2.0). Any license we adopt must be
compatible with distributing those dependencies bundled inside the package.

A choice was needed between:

- A single permissive license (MIT, or Apache-2.0).
- A dual `MIT OR Apache-2.0` grant, letting the consumer pick either.

## Decision

The project is licensed under **`MIT OR Apache-2.0`** (SPDX expression):
consumers may use it under the terms of **either** license, at their option.

- Two license files live at the repository root: `LICENSE-MIT` and
  `LICENSE-APACHE`.
- The copyright line reads `ivi-cli contributors`, matching the existing
  assembly `Copyright` / `Authors` metadata.
- Package metadata carries the SPDX expression centrally via
  `Directory.Build.props`:
  `<PackageLicenseExpression>MIT OR Apache-2.0</PackageLicenseExpression>`.
  NuGet renders this on the package page; no `PackageLicenseFile` is used
  (the expression and the file form are mutually exclusive, and the SPDX
  expression is the machine-readable source of truth).
- Contributions are, unless the contributor states otherwise, dual licensed
  under the same terms — stated in the README license section and governed by
  Apache-2.0 §5.

Rationale for the dual grant over a single license:

- **Patent protection.** Apache-2.0 §3 grants an explicit patent license and
  includes a patent-retaliation clause; MIT is silent on patents. Offering
  Apache-2.0 gives patent-cautious adopters an explicit grant.
- **Maximum compatibility.** MIT is the most frictionless license for the
  broadest set of downstream projects (including GPLv2 codebases, with which
  the Apache-2.0 patent terms are famously incompatible). Offering MIT as an
  option preserves that reach.
- **Ecosystem precedent.** `MIT OR Apache-2.0` is the de facto convention for
  cross-language infrastructure tooling; it sets clear, familiar expectations
  for contributors and packagers.
- **No copyleft surprise.** Both licenses are permissive, keeping the tool
  freely embeddable in commercial and closed test benches — the primary
  deployment context for a VISA/IVI instrument CLI.

## Consequences

**Pros**

- Downstream users get a well-understood permissive grant and can pick the
  license that fits their compliance posture.
- Explicit patent grant available via the Apache-2.0 option.
- Compatible with the bundled MIT/Apache-2.0 dependency set; distributing the
  self-contained binaries and the tool nupkg is unambiguously permitted.
- Aligns the repository with the OSS documentation policy (ADR 0024), which
  requires the README to state a license.

**Cons**

- Two license files and a dual-license contribution clause are marginally more
  to maintain than a single license.
- GitHub's license detector shows "View license" rather than a single SPDX
  badge for dual-licensed repositories.

**Mitigations**

- The SPDX expression `MIT OR Apache-2.0` in `Directory.Build.props` is the
  single canonical declaration; the two files are static boilerplate that does
  not change.
- The contribution terms are stated once in the README and rely on Apache-2.0
  §5, so no separate CLA process is introduced.
